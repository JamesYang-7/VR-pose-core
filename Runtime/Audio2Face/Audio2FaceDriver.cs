using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace VrChat.Audio2Face
{
    /// <summary>
    /// Drives a Character Creator character's face from the voice its <see cref="AudioSource"/> plays,
    /// with NVIDIA Audio2Face running live through the <c>a2f_unity</c> native plugin. Takes the place of
    /// <c>OVRLipSyncContext</c> + <c>CcVisemeDriver</c> on that character.
    ///
    /// <para>The audio thread (<see cref="OnAudioFilterRead"/>) downmixes the voice to 16 kHz mono and
    /// streams it to the model; Update collects the frames, samples them at the audio position that is
    /// audible now, maps the ARKit-style poses onto CC morphs (<see cref="CcMorphMixer"/>) and writes
    /// them to <see cref="Face"/> and <see cref="Tongue"/>. The Reallusion <c>BoneDriver</c> on
    /// <c>CC_Base_Body</c> then turns <c>Jaw_Open</c> into jaw rotation in its LateUpdate.</para>
    ///
    /// <para>The model looks ~0.27 s ahead in the audio, so a frame is ready only that long after its
    /// sound has played. With <see cref="SyncAudio"/> on, this component delays what the AudioSource
    /// plays by <see cref="AudioDelayMs"/> so that the lips and the voice line up; with it off the voice
    /// is immediate and the lips trail it.</para>
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class Audio2FaceDriver : MonoBehaviour
    {
        /// <summary>The model's <c>model.json</c>, relative to StreamingAssets.</summary>
        [field: SerializeField]
        [field: Tooltip("Regression model.json, relative to Assets/StreamingAssets (installed by Tools~/audio2face_plugin.py install-model).")]
        public string ModelPath { get; set; } = "Audio2Face/james/model.json";

        /// <summary>The CC body renderer (face morphs, and the BoneDriver that moves the jaw).</summary>
        [field: SerializeField]
        [field: Tooltip("CC_Base_Body: carries the face morphs and the BoneDriver that opens the jaw.")]
        public SkinnedMeshRenderer Face { get; set; }

        /// <summary>The CC tongue renderer.</summary>
        [field: SerializeField]
        [field: Tooltip("CC_Base_Tongue: carries the Tongue_* morphs. Leave empty on a character without one.")]
        public SkinnedMeshRenderer Tongue { get; set; }

        /// <summary>Frames per second the model generates.</summary>
        [field: SerializeField]
        [field: Range(15, 60)]
        [field: Tooltip("Frames per second Audio2Face generates. Frames are interpolated to the render rate. Applies on enable.")]
        public int FrameRate { get; set; } = 60;

        /// <summary>Scales every pose weight before the morph table is applied.</summary>
        [field: SerializeField]
        [field: Range(0f, 2f)]
        [field: Tooltip("Scales every Audio2Face pose weight before mapping (1 = as solved).")]
        public float Intensity { get; set; } = 1f;

        /// <summary>Delay the voice so that it lines up with the lips.</summary>
        [field: SerializeField]
        [field: Tooltip("Delay what the AudioSource plays by Audio Delay Ms so the voice lines up with the lips. Off: voice immediate, lips ~0.27 s late. Applies on enable.")]
        public bool SyncAudio { get; set; } = true;

        /// <summary>How far the voice is delayed when <see cref="SyncAudio"/> is on.</summary>
        [field: SerializeField]
        [field: Range(0, 1000)]
        [field: Tooltip("Voice delay in ms when Sync Audio is on. Set it to at least Latency Ms as shown while playing (measured ~290 ms at p95 on an RTX 4090). Applies on enable.")]
        public int AudioDelayMs { get; set; } = 300;

        /// <summary>What each Audio2Face pose becomes on the character.</summary>
        [field: SerializeField]
        [field: Tooltip("Audio2Face pose → CC morphs. Defaults are CcMorphMixer.DefaultBlends (CC5 Extended names). Applies on enable.")]
        public A2fPoseBlend[] Blends { get; set; } = CcMorphMixer.DefaultBlends();

        /// <summary>How far the newest frame is behind the audio pushed so far, in milliseconds.</summary>
        public float LatencyMs { get; private set; }

        /// <summary>True once the model has loaded and frames are flowing.</summary>
        public bool IsRunning => _session != null;

        // The audio thread reads these under _audioLock; the main thread writes them under it.
        readonly object _audioLock = new();
        Audio2FaceSession _session;
        AudioDownsampler _downsampler;
        AudioDelayLine _delayLine;
        float[] _downsampled = Array.Empty<float>();
        bool _syncAudio;
        int _delayFrames;
        int _delayMs; // snapshot taken on enable, so the voice delay and the frame delay always agree

        long _pushedSamples; // audio-thread written, main-thread read (Interlocked)
        long _delaySamples;
        long _newestFrame;

        FrameTimeline _timeline;
        float[] _poseWeights;
        float[] _morphWeights;
        MorphMixTerm[] _terms;
        SkinnedMeshRenderer[] _targetRenderers = Array.Empty<SkinnedMeshRenderer>();
        int[] _targetShapes = Array.Empty<int>();
        int[] _targetMorphs = Array.Empty<int>();
        CancellationTokenSource _opening;

        void OnEnable()
        {
            var outputRate = AudioSettings.outputSampleRate;
            lock (_audioLock)
            {
                _syncAudio = SyncAudio;
                _delayMs = SyncAudio ? AudioDelayMs : 0;
                _delayFrames = _delayMs * outputRate / 1000;
                _delayLine = null; // created on the audio thread, which knows the channel count
            }
            Interlocked.Exchange(ref _pushedSamples, 0);
            _opening = new CancellationTokenSource();
            _ = OpenAsync(Path.Combine(Application.streamingAssetsPath, ModelPath), outputRate, _opening.Token);
        }

        void OnDisable()
        {
            _opening?.Cancel();
            _opening?.Dispose();
            _opening = null;
            Audio2FaceSession session;
            lock (_audioLock)
            {
                session = _session;
                _session = null;
                _downsampler = null;
            }
            session?.Dispose();
            ResetMorphs();
        }

        void Update()
        {
            var session = _session;
            if (session == null) return;
            try
            {
                while (session.TryPopFrame(_poseWeights, out var timestamp))
                {
                    _timeline.Add(timestamp, _poseWeights);
                    _newestFrame = timestamp;
                }
            }
            catch (Audio2FaceException e)
            {
                Debug.LogError($"[Audio2FaceDriver] Audio2Face stopped: {e.Message}", this);
                enabled = false;
                return;
            }

            var pushed = Interlocked.Read(ref _pushedSamples);
            if (_timeline.Count > 0) LatencyMs = (pushed - _newestFrame) * 1000f / session.SampleRate;
            if (!_timeline.Sample(pushed - _delaySamples, _poseWeights)) return;

            CcMorphMixer.Mix(_terms, _poseWeights, Intensity, _morphWeights);
            for (var t = 0; t < _targetRenderers.Length; t++)
                _targetRenderers[t].SetBlendShapeWeight(_targetShapes[t], _morphWeights[_targetMorphs[t]] * 100f);
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            lock (_audioLock)
            {
                if (_session != null)
                {
                    var needed = _downsampler.MaxOutputCount(data.Length / channels);
                    if (_downsampled.Length < needed) _downsampled = new float[needed];
                    var count = _downsampler.Process(data, channels, _downsampled);
                    // A failed push is reported by TryPopFrame on the main thread.
                    if (_session.PushAudio(_downsampled.AsSpan(0, count))) Interlocked.Add(ref _pushedSamples, count);
                }

                if (_syncAudio)
                {
                    if (_delayLine == null || _delayLine.Channels != channels) _delayLine = new AudioDelayLine(_delayFrames, channels);
                    _delayLine.Process(data);
                }
            }
        }

        async Awaitable OpenAsync(string modelPath, int outputRate, CancellationToken ct)
        {
            Audio2FaceSession session = null;
            string error = null;
            var frameRate = FrameRate;
            // Loading the model takes ~0.6 s; keep it off the main thread.
            await Awaitable.BackgroundThreadAsync();
            try
            {
                session = Audio2FaceSession.Open(modelPath, frameRate);
            }
            catch (Exception e)
            {
                error = e.Message;
            }
            await Awaitable.MainThreadAsync();

            if (ct.IsCancellationRequested)
            {
                session?.Dispose();
                return;
            }
            if (session == null)
            {
                Debug.LogError($"[Audio2FaceDriver] {error}", this);
                return;
            }
            // OpenAsync is fire-and-forget (started from OnEnable), so nothing would observe an exception.
            try
            {
                Bind(session, outputRate, frameRate);
            }
            catch (Exception e)
            {
                session.Dispose();
                Debug.LogException(e, this);
            }
        }

        void Bind(Audio2FaceSession session, int outputRate, int frameRate)
        {
            var morphs = new Dictionary<string, int>(StringComparer.Ordinal);
            var renderers = new List<SkinnedMeshRenderer>();
            var shapes = new List<int>();
            var targetMorphs = new List<int>();

            // A morph is written to every assigned renderer that has it.
            int MorphIndexOf(string name)
            {
                if (morphs.TryGetValue(name, out var known)) return known;
                var morph = morphs.Count;
                var found = false;
                foreach (var renderer in new[] { Face, Tongue })
                {
                    var shape = renderer != null && renderer.sharedMesh != null ? renderer.sharedMesh.GetBlendShapeIndex(name) : -1;
                    if (shape < 0) continue;
                    renderers.Add(renderer);
                    shapes.Add(shape);
                    targetMorphs.Add(morph);
                    found = true;
                }
                if (!found) return -1;
                morphs.Add(name, morph);
                return morph;
            }

            var unresolved = new List<string>();
            _terms = CcMorphMixer.Plan(Blends, session.PoseNames, MorphIndexOf, unresolved);
            if (unresolved.Count > 0)
                Debug.LogWarning($"[Audio2FaceDriver] Skipped table entries the model or character lacks: {string.Join(", ", unresolved)}", this);

            _targetRenderers = renderers.ToArray();
            _targetShapes = shapes.ToArray();
            _targetMorphs = targetMorphs.ToArray();
            _poseWeights = new float[session.PoseNames.Count];
            _morphWeights = new float[morphs.Count];
            _timeline = new FrameTimeline(session.PoseNames.Count, 2 * frameRate);
            _delaySamples = (long)_delayMs * session.SampleRate / 1000;
            _newestFrame = 0;
            LatencyMs = 0f;

            var downsampler = new AudioDownsampler(outputRate, session.SampleRate);
            lock (_audioLock)
            {
                Interlocked.Exchange(ref _pushedSamples, 0);
                _downsampler = downsampler;
                _session = session;
            }
        }

        void ResetMorphs()
        {
            for (var t = 0; t < _targetRenderers.Length; t++)
                if (_targetRenderers[t] != null) _targetRenderers[t].SetBlendShapeWeight(_targetShapes[t], 0f);
        }
    }
}
