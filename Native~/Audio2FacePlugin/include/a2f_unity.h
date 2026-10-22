// Flat C API over the NVIDIA Audio2X SDK for Unity (P/Invoke).
//
// One session drives one face: it takes a live 16 kHz mono audio stream and
// returns per-frame blendshape weights (the SDK's ARKit-style skin poses, then
// its tongue poses). The SDK itself is a C++ API (virtual interfaces,
// std::error_code returns), which C# cannot call, hence this layer.
//
// Threading: a2f_push_audio may be called from any thread (Unity calls it from
// the audio thread in OnAudioFilterRead); inference runs on a worker thread the
// session owns; a2f_pop_frame is meant for the main thread. Every other call
// must not overlap a2f_destroy on the same session.
//
// Errors: functions returning int32_t return a negative value on failure, and
// a2f_last_error then describes it (per calling thread). a2f_create returns
// NULL on failure.
#pragma once

#include <stdint.h>

#ifdef A2F_UNITY_EXPORTS
#define A2F_UNITY_API __declspec(dllexport)
#else
#define A2F_UNITY_API __declspec(dllimport)
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct A2fSession A2fSession;

typedef struct A2fStats {
    int64_t pushedSamples;        // samples received by a2f_push_audio
    int64_t retainedSamples;      // samples the SDK still holds; stays bounded while the session keeps up
    int64_t producedFrames;       // frames the SDK has delivered
    int64_t droppedFrames;        // frames discarded because the queue was full (nobody popped them)
    int64_t latestFrameTimestamp; // sample index of the newest delivered frame, -1 before the first
} A2fStats;

// Loads audio2x.dll from this plugin's folder together with its CUDA and
// TensorRT dependencies. Returns 0 on success; otherwise writes which DLL is
// missing to `message`. a2f_create calls this itself; call it first to report
// a missing runtime without trying to load a model.
A2F_UNITY_API int32_t a2f_check_runtime(char* message, int32_t messageSize);

// Creates a session from a regression model's model.json (UTF-8 path) that
// outputs frames at frameRateNumerator / frameRateDenominator fps.
// Emotion is held neutral.
A2F_UNITY_API A2fSession* a2f_create(const char* modelJsonPath, int32_t frameRateNumerator, int32_t frameRateDenominator);

// Stops the worker and frees the session. NULL is ignored.
A2F_UNITY_API void a2f_destroy(A2fSession* session);

// Sample rate a2f_push_audio expects (16000 for the shipped models).
A2F_UNITY_API int32_t a2f_sample_rate(const A2fSession* session);

// Number of weights per frame: a2f_skin_weight_count skin poses, then the tongue poses.
A2F_UNITY_API int32_t a2f_weight_count(const A2fSession* session);
A2F_UNITY_API int32_t a2f_skin_weight_count(const A2fSession* session);

// Writes the pose name of weight `index` (NUL-terminated, truncated to fit) and
// returns its full length.
A2F_UNITY_API int32_t a2f_weight_name(const A2fSession* session, int32_t index, char* buffer, int32_t bufferSize);

// Queues mono samples at a2f_sample_rate. Returns 0, or a negative value if the
// session has failed. Never blocks on inference.
A2F_UNITY_API int32_t a2f_push_audio(A2fSession* session, const float* samples, int32_t count);

// Pops the oldest pending frame into `weights` (capacity >= a2f_weight_count)
// and its audio timestamp (sample index of the frame) into `timestamp`.
// Returns 1 if a frame was written, 0 if none is pending, negative on failure.
A2F_UNITY_API int32_t a2f_pop_frame(A2fSession* session, float* weights, int32_t capacity, int64_t* timestamp);

A2F_UNITY_API int32_t a2f_get_stats(const A2fSession* session, A2fStats* stats);

// Writes the calling thread's last error message and returns its full length.
A2F_UNITY_API int32_t a2f_last_error(char* buffer, int32_t bufferSize);

#ifdef __cplusplus
}
#endif
