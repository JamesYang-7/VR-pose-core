#include "a2f_unity.h"

#include "audio2face/audio2face.h"
#include "audio2x/cuda_utils.h"

#include <windows.h>
#include <delayimp.h>

#include <algorithm>
#include <atomic>
#include <condition_variable>
#include <cstring>
#include <deque>
#include <exception>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace {

// Frames waiting for a2f_pop_frame. Unity pops every frame, so this only fills up
// when nobody is popping (e.g. the component is paused); the oldest are dropped.
constexpr std::size_t kMaxPendingFrames = 256;

thread_local std::string t_lastError;

void SetError(std::string message) { t_lastError = std::move(message); }

int32_t CopyString(const std::string& text, char* buffer, int32_t bufferSize) {
    if (buffer != nullptr && bufferSize > 0) {
        const auto n = std::min<std::size_t>(text.size(), static_cast<std::size_t>(bufferSize) - 1);
        std::memcpy(buffer, text.data(), n);
        buffer[n] = '\0';
    }
    return static_cast<int32_t>(text.size());
}

std::string Describe(const char* what, std::error_code error) {
    return std::string(what) + ": " + error.message() + " (" + std::to_string(error.value()) + ")";
}

//
// Loading audio2x.dll.
//
// audio2x.dll is delay-loaded. Linking it normally would make Windows resolve it
// (and its CUDA/TensorRT imports) when Unity loads this plugin, and a missing
// TensorRT DLL would surface only as a bare DllNotFoundException for
// a2f_unity.dll. Instead it is loaded explicitly from this plugin's folder before
// the first SDK call, and the hook below hands that module to the delay-load
// helper, so a failure can name the missing dependency.
//

std::mutex g_loadMutex;
HMODULE g_audio2x = nullptr;

FARPROC WINAPI DelayLoadHook(unsigned notify, PDelayLoadInfo info) {
    if (notify == dliNotePreLoadLibrary && g_audio2x != nullptr && _stricmp(info->szDll, "audio2x.dll") == 0) {
        return reinterpret_cast<FARPROC>(g_audio2x);
    }
    return nullptr;
}

std::wstring PluginDirectory() {
    HMODULE self = nullptr;
    GetModuleHandleExW(
        GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&PluginDirectory), &self);
    std::wstring path(MAX_PATH, L'\0');
    const DWORD n = GetModuleFileNameW(self, path.data(), static_cast<DWORD>(path.size()));
    path.resize(n);
    return path.substr(0, path.find_last_of(L"\\/"));
}

std::string Narrow(const std::wstring& text) {
    const int n = WideCharToMultiByte(CP_UTF8, 0, text.c_str(), -1, nullptr, 0, nullptr, nullptr);
    std::string out(n > 0 ? n - 1 : 0, '\0');
    WideCharToMultiByte(CP_UTF8, 0, text.c_str(), -1, out.data(), n, nullptr, nullptr);
    return out;
}

bool EnsureRuntimeLoaded(std::string& error) {
    std::lock_guard<std::mutex> lock(g_loadMutex);
    if (g_audio2x != nullptr) return true;

    const std::wstring path = PluginDirectory() + L"\\audio2x.dll";
    // LOAD_WITH_ALTERED_SEARCH_PATH: resolve audio2x.dll's imports from its own
    // folder first, then the standard search (system folders, PATH).
    g_audio2x = LoadLibraryExW(path.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
    if (g_audio2x != nullptr) return true;

    const DWORD code = GetLastError();
    std::string missing;
    for (const wchar_t* dependency : {L"cudart64_12.dll", L"cublas64_12.dll", L"cublasLt64_12.dll",
                                      L"curand64_10.dll", L"nvinfer_10.dll", L"nvinfer_plugin_10.dll"}) {
        HMODULE probe = LoadLibraryExW(dependency, nullptr, 0);
        if (probe == nullptr) {
            missing += (missing.empty() ? "" : ", ") + Narrow(dependency);
        } else {
            FreeLibrary(probe);
        }
    }
    error = "Could not load " + Narrow(path) + " (Win32 error " + std::to_string(code) + "). ";
    error += missing.empty()
        ? std::string("Check that the file exists next to a2f_unity.dll.")
        : "Not found on the DLL search path: " + missing +
              ". Put the CUDA 12.x and TensorRT 10.x bin folders on PATH and restart Unity Hub and the editor.";
    return false;
}

struct Destroyer {
    template <typename T> void operator()(T* object) const { object->Destroy(); }
};

struct Frame {
    int64_t timestamp;
    std::vector<float> weights;
};

} // namespace

extern "C" const PfnDliHook __pfnDliNotifyHook2 = DelayLoadHook;

struct A2fSession {
    std::unique_ptr<nva2f::IBlendshapeExecutorBundle, Destroyer> bundle;
    std::vector<std::string> names;
    std::size_t skinCount = 0;
    int32_t sampleRate = 0;

    std::mutex inboxMutex;
    std::condition_variable inboxReady;
    std::vector<float> inbox;
    bool stopping = false;
    std::atomic<int64_t> pushedSamples{0};

    mutable std::mutex outboxMutex;
    std::deque<Frame> outbox;
    int64_t producedFrames = 0;
    int64_t droppedFrames = 0;
    int64_t latestFrameTimestamp = -1;
    std::atomic<int64_t> retainedSamples{0};

    mutable std::mutex errorMutex;
    std::string error;
    std::atomic<bool> failed{false};

    std::thread worker;

    void Fail(std::string message) {
        {
            std::lock_guard<std::mutex> lock(errorMutex);
            if (error.empty()) error = std::move(message);
        }
        failed = true;
    }

    std::string Error() const {
        std::lock_guard<std::mutex> lock(errorMutex);
        return error;
    }

    static void OnResults(void* userdata, const nva2f::IBlendshapeExecutor::HostResults& results, std::error_code errorCode) {
        auto& session = *static_cast<A2fSession*>(userdata);
        if (errorCode) {
            session.Fail(Describe("Blendshape solve failed", errorCode));
            return;
        }
        Frame frame{results.timeStampCurrentFrame,
                    std::vector<float>(results.weights.Data(), results.weights.Data() + results.weights.Size())};
        std::lock_guard<std::mutex> lock(session.outboxMutex);
        if (session.outbox.size() >= kMaxPendingFrames) {
            session.outbox.pop_front();
            ++session.droppedFrames;
        }
        session.outbox.push_back(std::move(frame));
        ++session.producedFrames;
        session.latestFrameTimestamp = results.timeStampCurrentFrame;
    }

    void Run() {
        if (const auto ec = nva2x::SetCudaDeviceIfNeeded(0)) {
            Fail(Describe("Could not select CUDA device 0", ec));
            return;
        }
        auto& executor = bundle->GetExecutor();
        auto& audio = bundle->GetAudioAccumulator(0);
        const auto stream = bundle->GetCudaStream().Data();
        std::vector<float> chunk;
        while (true) {
            {
                std::unique_lock<std::mutex> lock(inboxMutex);
                inboxReady.wait(lock, [this] { return stopping || !inbox.empty(); });
                if (stopping) return;
                chunk.swap(inbox);
            }
            if (const auto ec = audio.Accumulate(nva2x::HostTensorFloatConstView{chunk.data(), chunk.size()}, stream)) {
                Fail(Describe("Could not accumulate audio", ec));
                return;
            }
            chunk.clear();

            while (nva2x::GetNbReadyTracks(executor) > 0) {
                if (const auto ec = executor.Execute(nullptr)) {
                    Fail(Describe("Inference failed", ec));
                    return;
                }
            }
            // The CPU solver can deliver results after Execute returns; wait so that
            // every frame this audio produced is queued before the next chunk.
            if (const auto ec = executor.Wait(0)) {
                Fail(Describe("Blendshape solve failed", ec));
                return;
            }
            // A live stream never closes, so audio the executor has moved past must
            // be dropped or the accumulator grows for the whole session.
            if (const auto ec = audio.DropSamplesBefore(executor.GetNextAudioSampleToRead(0))) {
                Fail(Describe("Could not drop consumed audio", ec));
                return;
            }
            retainedSamples = static_cast<int64_t>(audio.NbAccumulatedSamples() - audio.NbDroppedSamples());
        }
    }
};

namespace {

bool CheckSession(const A2fSession* session) {
    if (session == nullptr) {
        SetError("session is null");
        return false;
    }
    if (session->failed) {
        SetError(session->Error());
        return false;
    }
    return true;
}

std::unique_ptr<A2fSession> CreateSession(const char* modelJsonPath, int32_t fpsNumerator, int32_t fpsDenominator) {
    if (modelJsonPath == nullptr || fpsNumerator <= 0 || fpsDenominator <= 0) {
        SetError("modelJsonPath must be set and the frame rate positive");
        return nullptr;
    }
    if (const auto ec = nva2x::SetCudaDeviceIfNeeded(0)) {
        SetError(Describe("Could not select CUDA device 0", ec));
        return nullptr;
    }

    auto session = std::make_unique<A2fSession>();
    session->bundle.reset(nva2f::ReadRegressionBlendshapeSolveExecutorBundle(
        1, modelJsonPath, nva2f::IGeometryExecutor::ExecutionOption::SkinTongue,
        false, // CPU solver: its results arrive on the host, which is where Unity needs them.
        static_cast<std::size_t>(fpsNumerator), static_cast<std::size_t>(fpsDenominator), nullptr, nullptr));
    if (!session->bundle) {
        SetError(std::string("Could not load the regression model ") + modelJsonPath +
                 ". Check the path, and that network.trt was built for this GPU and TensorRT version.");
        return nullptr;
    }

    auto& executor = session->bundle->GetExecutor();
    if (executor.GetResultType() != nva2f::IBlendshapeExecutor::ResultsType::HOST) {
        SetError("The blendshape executor does not return host results");
        return nullptr;
    }
    if (const auto ec = executor.SetResultsCallback(&A2fSession::OnResults, session.get())) {
        SetError(Describe("Could not set the results callback", ec));
        return nullptr;
    }

    // Neutral emotion for the whole stream: one zero vector at t = 0, then closed,
    // so the executor never waits for emotions.
    auto& emotions = session->bundle->GetEmotionAccumulator(0);
    const std::vector<float> neutral(emotions.GetEmotionSize(), 0.0f);
    const auto stream = session->bundle->GetCudaStream().Data();
    if (const auto ec = emotions.Accumulate(0, nva2x::HostTensorFloatConstView{neutral.data(), neutral.size()}, stream)) {
        SetError(Describe("Could not set the neutral emotion", ec));
        return nullptr;
    }
    if (const auto ec = emotions.Close()) {
        SetError(Describe("Could not close the emotion accumulator", ec));
        return nullptr;
    }

    for (const bool tongue : {false, true}) {
        nva2f::IBlendshapeSolver* solver = nullptr;
        const auto ec = tongue ? nva2f::GetExecutorTongueSolver(executor, 0, &solver)
                               : nva2f::GetExecutorSkinSolver(executor, 0, &solver);
        if (ec || solver == nullptr) {
            if (!tongue) {
                SetError(ec ? Describe("Could not get the skin solver", ec) : std::string("The model has no skin solver"));
                return nullptr;
            }
            break; // No tongue solver: the weights are skin only.
        }
        const auto count = static_cast<std::size_t>(solver->NumBlendshapePoses());
        for (std::size_t i = 0; i < count; ++i) {
            const char* name = solver->GetPoseName(i);
            session->names.emplace_back(name != nullptr ? name : "");
        }
        if (!tongue) session->skinCount = count;
    }
    if (session->names.size() != executor.GetWeightCount()) {
        SetError("Pose names (" + std::to_string(session->names.size()) + ") do not match the weight count (" +
                 std::to_string(executor.GetWeightCount()) + ")");
        return nullptr;
    }

    session->sampleRate = static_cast<int32_t>(executor.GetSamplingRate());
    session->worker = std::thread([s = session.get()] { s->Run(); });
    return session;
}

} // namespace

extern "C" {

int32_t a2f_check_runtime(char* message, int32_t messageSize) {
    std::string error;
    if (EnsureRuntimeLoaded(error)) {
        CopyString("", message, messageSize);
        return 0;
    }
    SetError(error);
    CopyString(error, message, messageSize);
    return -1;
}

A2fSession* a2f_create(const char* modelJsonPath, int32_t frameRateNumerator, int32_t frameRateDenominator) {
    std::string error;
    if (!EnsureRuntimeLoaded(error)) {
        SetError(error);
        return nullptr;
    }
    try {
        return CreateSession(modelJsonPath, frameRateNumerator, frameRateDenominator).release();
    } catch (const std::exception& e) {
        SetError(std::string("a2f_create: ") + e.what());
    } catch (...) {
        SetError("a2f_create: unknown exception");
    }
    return nullptr;
}

void a2f_destroy(A2fSession* session) {
    if (session == nullptr) return;
    {
        std::lock_guard<std::mutex> lock(session->inboxMutex);
        session->stopping = true;
    }
    session->inboxReady.notify_one();
    if (session->worker.joinable()) session->worker.join();
    if (session->bundle) session->bundle->GetExecutor().Wait(0); // no callback may run after delete
    delete session;
}

int32_t a2f_sample_rate(const A2fSession* session) {
    return session != nullptr ? session->sampleRate : (SetError("session is null"), -1);
}

int32_t a2f_weight_count(const A2fSession* session) {
    return session != nullptr ? static_cast<int32_t>(session->names.size()) : (SetError("session is null"), -1);
}

int32_t a2f_skin_weight_count(const A2fSession* session) {
    return session != nullptr ? static_cast<int32_t>(session->skinCount) : (SetError("session is null"), -1);
}

int32_t a2f_weight_name(const A2fSession* session, int32_t index, char* buffer, int32_t bufferSize) {
    if (session == nullptr || index < 0 || static_cast<std::size_t>(index) >= session->names.size()) {
        SetError("session is null or index out of range");
        return -1;
    }
    return CopyString(session->names[static_cast<std::size_t>(index)], buffer, bufferSize);
}

int32_t a2f_push_audio(A2fSession* session, const float* samples, int32_t count) {
    if (!CheckSession(session)) return -1;
    if (count <= 0) return 0;
    if (samples == nullptr) {
        SetError("samples is null");
        return -1;
    }
    try {
        {
            std::lock_guard<std::mutex> lock(session->inboxMutex);
            session->inbox.insert(session->inbox.end(), samples, samples + count);
        }
        session->pushedSamples += count;
        session->inboxReady.notify_one();
        return 0;
    } catch (const std::exception& e) {
        SetError(std::string("a2f_push_audio: ") + e.what());
        return -1;
    }
}

int32_t a2f_pop_frame(A2fSession* session, float* weights, int32_t capacity, int64_t* timestamp) {
    if (!CheckSession(session)) return -1;
    if (weights == nullptr || capacity < static_cast<int32_t>(session->names.size())) {
        SetError("weights is null or smaller than a2f_weight_count");
        return -1;
    }
    Frame frame;
    {
        std::lock_guard<std::mutex> lock(session->outboxMutex);
        if (session->outbox.empty()) return 0;
        frame = std::move(session->outbox.front());
        session->outbox.pop_front();
    }
    std::copy(frame.weights.begin(), frame.weights.end(), weights);
    if (timestamp != nullptr) *timestamp = frame.timestamp;
    return 1;
}

int32_t a2f_get_stats(const A2fSession* session, A2fStats* stats) {
    if (session == nullptr || stats == nullptr) {
        SetError("session or stats is null");
        return -1;
    }
    stats->pushedSamples = session->pushedSamples;
    stats->retainedSamples = session->retainedSamples;
    std::lock_guard<std::mutex> lock(session->outboxMutex);
    stats->producedFrames = session->producedFrames;
    stats->droppedFrames = session->droppedFrames;
    stats->latestFrameTimestamp = session->latestFrameTimestamp;
    return 0;
}

int32_t a2f_last_error(char* buffer, int32_t bufferSize) {
    return CopyString(t_lastError, buffer, bufferSize);
}

} // extern "C"
