// Exercises a2f_unity.dll the way the Unity component uses it, without Unity.
//
//   a2f_unity_test <model.json> <speech.wav> [--realtime] [--long <long.wav>]
//
// speech.wav / long.wav: 16 kHz mono 16-bit PCM. --realtime paces the pushes
// like Unity's audio thread does and reports the audio-to-frame latency.
// --long streams a long file to check that the session's memory stays bounded.
// Exit code 0 when every check passes.

#include "a2f_unity.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <iterator>
#include <string>
#include <thread>
#include <vector>

namespace {

int g_failures = 0;

void Check(bool condition, const std::string& what) {
    std::printf("  [%s] %s\n", condition ? "PASS" : "FAIL", what.c_str());
    if (!condition) ++g_failures;
}

std::string LastError() {
    char buffer[2048];
    a2f_last_error(buffer, sizeof(buffer));
    return buffer;
}

// Minimal RIFF reader: 16-bit PCM, mono, 16 kHz only.
bool ReadWav(const char* path, std::vector<float>& samples) {
    std::ifstream file(path, std::ios::binary);
    if (!file) return false;
    std::vector<char> bytes((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
    if (bytes.size() < 12 || std::memcmp(bytes.data(), "RIFF", 4) != 0 || std::memcmp(bytes.data() + 8, "WAVE", 4) != 0) return false;
    uint16_t format = 0, channels = 0, bits = 0;
    uint32_t rate = 0;
    for (std::size_t pos = 12; pos + 8 <= bytes.size();) {
        uint32_t size = 0;
        std::memcpy(&size, bytes.data() + pos + 4, 4);
        const char* body = bytes.data() + pos + 8;
        if (std::memcmp(bytes.data() + pos, "fmt ", 4) == 0) {
            std::memcpy(&format, body, 2);
            std::memcpy(&channels, body + 2, 2);
            std::memcpy(&rate, body + 4, 4);
            std::memcpy(&bits, body + 14, 2);
        } else if (std::memcmp(bytes.data() + pos, "data", 4) == 0) {
            if (format != 1 || channels != 1 || bits != 16 || rate != 16000) return false;
            const auto count = std::min<std::size_t>(size, bytes.size() - pos - 8) / 2;
            samples.resize(count);
            for (std::size_t i = 0; i < count; ++i) {
                int16_t s;
                std::memcpy(&s, body + 2 * i, 2);
                samples[i] = s / 32768.0f;
            }
            return true;
        }
        pos += 8 + size + (size & 1);
    }
    return false;
}

struct Frame {
    int64_t timestamp;
    int64_t pushedWhenPopped;
    std::vector<float> weights;
};

// Unity's audio thread delivers ~21 ms blocks (1024 frames at 48 kHz).
constexpr int kChunk = 341;
constexpr int kFps = 60;

// Pushes `audio` in Unity-sized chunks from a producer thread while this thread
// pops frames, then pushes `tailSeconds` of silence and drains until a frame at
// or past the end of `audio` arrives.
std::vector<Frame> Stream(A2fSession* session, const std::vector<float>& audio, bool realtime, double tailSeconds) {
    const int weightCount = a2f_weight_count(session);
    const int rate = a2f_sample_rate(session);
    const std::vector<float> silence(static_cast<std::size_t>(tailSeconds * rate), 0.0f);
    std::vector<Frame> frames;
    std::atomic<int64_t> pushed{0};
    std::atomic<bool> done{false};

    std::thread producer([&] {
        const auto start = std::chrono::steady_clock::now();
        for (const auto* buffer : {&audio, &silence}) {
            for (std::size_t i = 0; i < buffer->size(); i += kChunk) {
                const int n = static_cast<int>(std::min<std::size_t>(kChunk, buffer->size() - i));
                if (a2f_push_audio(session, buffer->data() + i, n) != 0) {
                    std::printf("  push failed: %s\n", LastError().c_str());
                    done = true;
                    return;
                }
                pushed += n;
                if (realtime) std::this_thread::sleep_until(start + std::chrono::microseconds(pushed * 1000000 / rate));
            }
        }
        done = true;
    });

    std::vector<float> weights(static_cast<std::size_t>(weightCount));
    const auto deadline = std::chrono::steady_clock::now() +
        std::chrono::seconds(static_cast<int>((audio.size() + silence.size()) / rate) + 30);
    while (std::chrono::steady_clock::now() < deadline) {
        int64_t timestamp = 0;
        const int status = a2f_pop_frame(session, weights.data(), weightCount, &timestamp);
        if (status < 0) {
            std::printf("  pop failed: %s\n", LastError().c_str());
            break;
        }
        if (status == 1) {
            frames.push_back({timestamp, pushed.load(), weights});
            if (timestamp >= static_cast<int64_t>(audio.size())) break;
            continue;
        }
        if (done && pushed == static_cast<int64_t>(audio.size() + silence.size())) {
            A2fStats stats{};
            a2f_get_stats(session, &stats);
            if (stats.latestFrameTimestamp >= static_cast<int64_t>(audio.size())) continue; // drain what's queued
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    producer.join();
    return frames;
}

int FindWeight(A2fSession* session, const char* name) {
    char buffer[256];
    for (int i = 0; i < a2f_weight_count(session); ++i) {
        a2f_weight_name(session, i, buffer, sizeof(buffer));
        if (std::strcmp(buffer, name) == 0) return i;
    }
    return -1;
}

} // namespace

int main(int argc, char** argv) {
    if (argc < 3) {
        std::printf("usage: %s <model.json> <speech.wav> [--realtime] [--long <long.wav>]\n", argv[0]);
        return 2;
    }
    const char* modelPath = argv[1];
    bool realtime = false;
    const char* longPath = nullptr;
    for (int i = 3; i < argc; ++i) {
        if (std::strcmp(argv[i], "--realtime") == 0) realtime = true;
        else if (std::strcmp(argv[i], "--long") == 0 && i + 1 < argc) longPath = argv[++i];
    }
    std::vector<float> speech;
    if (!ReadWav(argv[2], speech)) {
        std::printf("Could not read %s (need 16 kHz mono 16-bit PCM)\n", argv[2]);
        return 2;
    }

    std::printf("Runtime\n");
    char message[2048];
    const bool runtimeOk = a2f_check_runtime(message, sizeof(message)) == 0;
    Check(runtimeOk, runtimeOk ? "audio2x.dll and its CUDA/TensorRT dependencies load" : message);
    if (!runtimeOk) return 1;

    std::printf("Create\n");
    const auto createStart = std::chrono::steady_clock::now();
    A2fSession* session = a2f_create(modelPath, kFps, 1);
    const double createMs = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - createStart).count();
    Check(session != nullptr, session ? "a2f_create (" + std::to_string(static_cast<int>(createMs)) + " ms)" : "a2f_create: " + LastError());
    if (session == nullptr) return 1;

    const int weightCount = a2f_weight_count(session);
    const int skinCount = a2f_skin_weight_count(session);
    const int rate = a2f_sample_rate(session);
    Check(rate == 16000, "sample rate " + std::to_string(rate));
    Check(skinCount > 0 && weightCount > skinCount, std::to_string(skinCount) + " skin + " + std::to_string(weightCount - skinCount) + " tongue weights");
    std::string names;
    char buffer[256];
    for (int i = 0; i < weightCount; ++i) {
        a2f_weight_name(session, i, buffer, sizeof(buffer));
        names += (i == skinCount ? " | " : (i ? " " : "")) + std::string(buffer);
    }
    std::printf("  names: %s\n", names.c_str());
    const int jawOpen = FindWeight(session, "jawOpen");
    Check(jawOpen >= 0 && jawOpen < skinCount, "jawOpen is a skin weight");

    std::printf("Stream %.1f s of speech%s\n", speech.size() / double(rate), realtime ? " in real time" : "");
    const auto frames = Stream(session, speech, realtime, 1.0);
    const auto expected = static_cast<int64_t>(speech.size()) * kFps / rate;
    Check(static_cast<int64_t>(frames.size()) >= expected, std::to_string(frames.size()) + " frames (>= " + std::to_string(expected) + ")");

    bool monotonic = true, finite = true, inRange = true;
    const double step = double(rate) / kFps;
    for (std::size_t i = 0; i < frames.size(); ++i) {
        if (i > 0) {
            const double d = double(frames[i].timestamp - frames[i - 1].timestamp);
            if (d < std::floor(step) || d > std::ceil(step)) monotonic = false;
        }
        for (int w = 0; w < weightCount; ++w) {
            const float v = frames[i].weights[w];
            if (!std::isfinite(v)) finite = false;
            if (w < skinCount && (v < -0.01f || v > 1.01f)) inRange = false;
        }
    }
    Check(monotonic, "timestamps advance one frame (" + std::to_string(step) + " samples) at a time");
    Check(finite, "all weights finite");
    Check(inRange, "skin weights within [0, 1]");

    float jawMax = 0.0f;
    double jawTail = 0.0;
    int tailCount = 0;
    for (const auto& f : frames) {
        if (f.timestamp < static_cast<int64_t>(speech.size())) jawMax = std::max(jawMax, f.weights[jawOpen]);
    }
    for (const auto& f : frames) {
        if (f.timestamp >= static_cast<int64_t>(speech.size()) - rate / 10) {
            jawTail += f.weights[jawOpen];
            ++tailCount;
        }
    }
    Check(jawMax > 0.2f, "jawOpen opens during speech (max " + std::to_string(jawMax) + ")");
    if (tailCount > 0) std::printf("  jawOpen at the end of the clip: %.3f\n", jawTail / tailCount);

    if (realtime && !frames.empty()) {
        std::vector<double> lags;
        for (const auto& f : frames) lags.push_back((f.pushedWhenPopped - f.timestamp) * 1000.0 / rate);
        std::sort(lags.begin(), lags.end());
        std::printf("  latency (audio pushed - frame time): median %.0f ms, p95 %.0f ms, max %.0f ms\n",
                    lags[lags.size() / 2], lags[lags.size() * 95 / 100], lags.back());
    }
    A2fStats stats{};
    a2f_get_stats(session, &stats);
    std::printf("  stats: pushed %lld, retained %lld, produced %lld, dropped %lld\n",
                (long long)stats.pushedSamples, (long long)stats.retainedSamples,
                (long long)stats.producedFrames, (long long)stats.droppedFrames);
    Check(stats.droppedFrames == 0, "no frames dropped");

    std::printf("Destroy and recreate (a second Play session)\n");
    a2f_destroy(session);
    session = a2f_create(modelPath, kFps, 1);
    Check(session != nullptr, session ? "second a2f_create" : "second a2f_create: " + LastError());
    if (session != nullptr) {
        const std::vector<float> second(speech.begin(), speech.begin() + std::min<std::size_t>(speech.size(), rate));
        const auto again = Stream(session, second, false, 1.0);
        Check(!again.empty() && again.front().timestamp == frames.front().timestamp,
              "the new session starts again from timestamp " + std::to_string(frames.front().timestamp));
    }

    if (longPath != nullptr && session != nullptr) {
        std::vector<float> longAudio;
        if (!ReadWav(longPath, longAudio)) {
            Check(false, std::string("read ") + longPath);
        } else {
            a2f_destroy(session);
            session = a2f_create(modelPath, kFps, 1);
            std::printf("Stream %.0f s as fast as possible\n", longAudio.size() / double(rate));
            const auto start = std::chrono::steady_clock::now();
            const auto longFrames = Stream(session, longAudio, false, 1.0);
            const double seconds = std::chrono::duration<double>(std::chrono::steady_clock::now() - start).count();
            a2f_get_stats(session, &stats);
            std::printf("  %zu frames in %.1f s (%.0fx real time); retained %lld samples, dropped %lld frames\n",
                        longFrames.size(), seconds, longAudio.size() / double(rate) / seconds,
                        (long long)stats.retainedSamples, (long long)stats.droppedFrames);
            Check(static_cast<int64_t>(longFrames.size()) >= static_cast<int64_t>(longAudio.size()) * kFps / rate, "every frame of the long stream arrived");
            Check(stats.retainedSamples < 2 * rate, "retained audio stays under 2 s (" + std::to_string(stats.retainedSamples) + " samples)");
        }
    }

    a2f_destroy(session);
    std::printf("%s (%d failed)\n", g_failures == 0 ? "ALL PASSED" : "FAILED", g_failures);
    return g_failures == 0 ? 0 : 1;
}
