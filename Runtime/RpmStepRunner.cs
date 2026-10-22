using System;
using System.IO;
using Unity.InferenceEngine;

namespace VrChat
{
    /// <summary>
    /// The rolling loop around an RPM step graph (<c>Models/rpm/models/&lt;name&gt;/rpm_&lt;name&gt;.onnx</c>,
    /// RPM-README §3 and §5): keeps the last 11 input frames and the model's state, a normalised
    /// <c>motion_ctx</c> (the last 10 committed rows) and <c>prev_pred</c> (the L-frame rolling window), and runs one
    /// step per model frame.
    ///
    /// <para><b>Which frame a step commits.</b> The step on input frames <c>i-10 … i</c> commits frame <c>i</c>, the
    /// newest input frame: the first slot of the refined window (<c>rolling_latency</c> 0), placed on that frame's
    /// tracked head. So the first step runs on the 11th frame fed (model frame 10), and nothing is committed
    /// before it; <see cref="Begin"/> returns false until then. In the reference output that step's
    /// <see cref="MotionOut"/> is <c>ref_pred_motion[10]</c>, and rows 0–9 there are the evaluation's mocap seed.</para>
    ///
    /// <para><b>The state goes back unchanged.</b> The graph returns the next <c>motion_ctx</c> and <c>prev_pred</c>,
    /// which feed the next step exactly as returned. They are not rebuilt from <see cref="MotionOut"/>: on the
    /// feet, whose deviation is 0, the network writes values around 1e-6 that de-normalise to exactly identity, so
    /// a round trip through physical units would change the state.</para>
    ///
    /// <para>No canonicalisation: the network sees world positions as fed (RPM-README §7). <see cref="Step"/>
    /// blocks on the GPU; live code splits it into <see cref="Begin"/> and
    /// <see cref="TryComplete"/>.</para>
    /// </summary>
    public sealed class RpmStepRunner : IPoseStepRunner
    {
        const int kInputWidth = PoseLayout.InputWidth;
        const int kMotionWidth = PoseLayout.MotionWidth;
        const int kRotWidth = RpmManifest.RotationWidth;
        const int kJoints = PoseLayout.Joints;

        readonly Worker _worker;
        readonly Tensor<float> _tSparse, _tMotion, _tPrev, _tJoints;
        readonly float[][] _inputs;          // ring of the last InputFrames input frames
        readonly float[] _sparseWin;
        readonly float[] _motionCtx, _prevPred; // the state, normalised
        readonly float[] _standingRow = new float[kMotionWidth];
        readonly System.Diagnostics.Stopwatch _clock = new();
        int _inputNext, _inputCount;
        bool _started, _seedStanding;
        Tensor<float> _pendingMotion, _pendingRotations, _pendingMotionCtx, _pendingPrevPred; // null when idle
        double _beganMs;

        /// <summary>The version's windows and normalisation statistics.</summary>
        public RpmManifest Manifest { get; }

        /// <inheritdoc/>
        public float[] MotionOut { get; } = new float[kMotionWidth];

        /// <inheritdoc/>
        public float[] JointRotations { get; } = new float[kJoints * 9];

        /// <inheritdoc/>
        public int StepCount { get; private set; }

        /// <inheritdoc/>
        public bool IsStepPending => _pendingMotion != null;

        /// <inheritdoc/>
        public double LastMainThreadMs { get; private set; }

        /// <inheritdoc/>
        public double LastLatencyMs { get; private set; }

        /// <summary>The input frame whose pose the last step committed: the newest frame fed to it.</summary>
        public int LastCommittedFrame => StepCount == 0 ? -1 : Manifest.InputFrames - 1 + StepCount - 1;

        /// <exception cref="NotSupportedException">The version commits a frame other than the newest input frame.</exception>
        /// <exception cref="InvalidDataException">The graph's inputs don't match the manifest's windows.</exception>
        public RpmStepRunner(ModelAsset modelAsset, RpmManifest manifest, BackendType backend = BackendType.GPUCompute)
        {
            Manifest = manifest;
            if (manifest.RollingLatency != 0)
                throw new NotSupportedException($"RPM '{manifest.Name}': rolling_latency {manifest.RollingLatency}; only 0 (commit the newest input frame) is supported.");
            var model = ModelLoader.Load(modelAsset);
            CheckInput(model, "sparse_ctx", manifest.InputFrames, kInputWidth);
            CheckInput(model, "motion_ctx", manifest.StateFrames, kRotWidth);
            CheckInput(model, "prev_pred", manifest.PredictionWindow, kRotWidth);

            _worker = new Worker(model, backend);
            _tSparse = new Tensor<float>(new TensorShape(1, manifest.InputFrames, kInputWidth));
            _tMotion = new Tensor<float>(new TensorShape(1, manifest.StateFrames, kRotWidth));
            _tPrev = new Tensor<float>(new TensorShape(1, manifest.PredictionWindow, kRotWidth));
            _tJoints = new Tensor<float>(new TensorShape(1, kJoints, 3));
            _inputs = new float[manifest.InputFrames][];
            for (int i = 0; i < _inputs.Length; i++) _inputs[i] = new float[kInputWidth];
            _sparseWin = new float[manifest.InputFrames * kInputWidth];
            _motionCtx = new float[manifest.StateFrames * kRotWidth];
            _prevPred = new float[manifest.PredictionWindow * kRotWidth];
        }

        /// <summary>
        /// Resets the loop with the given state (normalised; <see cref="EvaluationState"/> reproduces the reference
        /// output). The next <see cref="Manifest"/>.InputFrames frames fill the input window; the last of them gets
        /// the first step.
        /// </summary>
        public void Start(float[] motionCtx, float[] prevPred, float[] jointsRest)
        {
            Reset(jointsRest);
            Array.Copy(motionCtx, _motionCtx, _motionCtx.Length);
            Array.Copy(prevPred, _prevPred, _prevPred.Length);
        }

        /// <summary>
        /// Resets the loop for live use: the state is seeded from the standing pose (<see cref="StandingState"/>) on
        /// the frame of the first step, once the input window is full.
        /// </summary>
        public void StartStanding(float[] jointsRest)
        {
            Reset(jointsRest);
            _seedStanding = true;
        }

        void Reset(float[] jointsRest)
        {
            if (IsStepPending) Complete(); // the state is about to be overwritten; just drain the GPU
            _inputNext = _inputCount = 0;
            _tJoints.Upload(jointsRest);
            StepCount = 0;
            _seedStanding = false;
            _started = true;
        }

        /// <summary>Feeds one input frame and, once the window is full, runs its step; blocks. Returns whether a step ran.</summary>
        public bool Step(float[] frame)
        {
            if (!Begin(frame)) return false;
            Complete();
            return true;
        }

        /// <inheritdoc/>
        public bool Begin(float[] frame)
        {
            if (!_started) throw new InvalidOperationException("Start or StartStanding must be called before Begin or Step.");
            if (IsStepPending) throw new InvalidOperationException("The previous step has not been collected yet.");
            _clock.Restart();
            Array.Copy(frame, _inputs[_inputNext], kInputWidth);
            _inputNext = (_inputNext + 1) % _inputs.Length;
            if (_inputCount < _inputs.Length) _inputCount++;
            if (_inputCount < _inputs.Length) return false;

            if (_seedStanding)
            {
                StandingState(frame, Manifest, _standingRow, _motionCtx, _prevPred);
                _seedStanding = false;
            }
            for (int r = 0; r < _inputs.Length; r++) // oldest first: frames i-10 … i
                Array.Copy(_inputs[(_inputNext + r) % _inputs.Length], 0, _sparseWin, r * kInputWidth, kInputWidth);
            _tSparse.Upload(_sparseWin);
            _tMotion.Upload(_motionCtx);
            _tPrev.Upload(_prevPred);
            _worker.SetInput("sparse_ctx", _tSparse);
            _worker.SetInput("motion_ctx", _tMotion);
            _worker.SetInput("prev_pred", _tPrev);
            _worker.SetInput("joints_rest", _tJoints);
            _worker.Schedule();

            _pendingMotion = (Tensor<float>)_worker.PeekOutput("motion_out");
            _pendingRotations = (Tensor<float>)_worker.PeekOutput("joint_rotmat");
            _pendingMotionCtx = (Tensor<float>)_worker.PeekOutput("next_motion_ctx");
            _pendingPrevPred = (Tensor<float>)_worker.PeekOutput("next_prev_pred");
            _pendingMotion.ReadbackRequest();
            _pendingRotations.ReadbackRequest();
            _pendingMotionCtx.ReadbackRequest();
            _pendingPrevPred.ReadbackRequest();
            _beganMs = _clock.Elapsed.TotalMilliseconds;
            LastMainThreadMs = _beganMs;
            return true;
        }

        /// <inheritdoc/>
        public bool TryComplete()
        {
            if (!IsStepPending || !_pendingMotion.IsReadbackRequestDone() || !_pendingRotations.IsReadbackRequestDone()
                || !_pendingMotionCtx.IsReadbackRequestDone() || !_pendingPrevPred.IsReadbackRequestDone()) return false;
            Finish();
            return true;
        }

        /// <inheritdoc/>
        public void Complete()
        {
            if (!IsStepPending) return;
            double waitStart = _clock.Elapsed.TotalMilliseconds;
            Finish();
            LastMainThreadMs = _beganMs + (_clock.Elapsed.TotalMilliseconds - waitStart);
        }

        void Finish()
        {
            Array.Copy(TensorReadback.Download(_pendingMotion), MotionOut, kMotionWidth);
            Array.Copy(TensorReadback.Download(_pendingRotations), JointRotations, JointRotations.Length);
            Array.Copy(TensorReadback.Download(_pendingMotionCtx), _motionCtx, _motionCtx.Length); // unchanged, see the class notes
            Array.Copy(TensorReadback.Download(_pendingPrevPred), _prevPred, _prevPred.Length);
            _pendingMotion = _pendingRotations = _pendingMotionCtx = _pendingPrevPred = null;
            LastLatencyMs = _clock.Elapsed.TotalMilliseconds;
            StepCount++;
        }

        /// <summary>
        /// The evaluation's seed (RPM-README §6): <c>motion_ctx</c> = normalised mocap rows 0 … 9, <c>prev_pred</c> =
        /// normalised mocap rows 10 … 10+L-1, from flat T × 198 <paramref name="groundTruth"/>. It reads mocap of
        /// frames after the first step, so it exists offline only.
        /// </summary>
        public static void EvaluationState(float[] groundTruth, RpmManifest manifest, float[] motionCtx, float[] prevPred)
        {
            for (int r = 0; r < manifest.StateFrames; r++)
                manifest.Normalise(groundTruth, r * kMotionWidth, motionCtx, r * kRotWidth);
            for (int r = 0; r < manifest.PredictionWindow; r++)
                manifest.Normalise(groundTruth, (manifest.StateFrames + r) * kMotionWidth, prevPred, r * kRotWidth);
        }

        /// <summary>
        /// The live seed (RPM-README §5): the rest pose stood upright and turned to the head's heading in
        /// <paramref name="frame"/>, the frame of the first step, normalised into every <c>motion_ctx</c> and
        /// <c>prev_pred</c> row. Its feet are exactly identity, their mean, so they normalise to exactly 0.
        /// <paramref name="scratch"/> holds 198 floats.
        /// </summary>
        public static void StandingState(float[] frame, RpmManifest manifest, float[] scratch, float[] motionCtx, float[] prevPred)
        {
            PoseLayout.StandingRotations(frame, scratch);
            manifest.Normalise(scratch, 0, motionCtx, 0);
            for (int r = 1; r < manifest.StateFrames; r++) Array.Copy(motionCtx, 0, motionCtx, r * kRotWidth, kRotWidth);
            for (int r = 0; r < manifest.PredictionWindow; r++) Array.Copy(motionCtx, 0, prevPred, r * kRotWidth, kRotWidth);
        }

        static void CheckInput(Model model, string name, int frames, int width)
        {
            foreach (var input in model.inputs)
            {
                if (input.name != name) continue;
                if (!input.shape.IsStatic()) break;
                var shape = input.shape.ToTensorShape();
                if (shape.rank == 3 && shape[0] == 1 && shape[1] == frames && shape[2] == width) return;
                throw new InvalidDataException($"RPM graph input '{name}' has shape {shape}; the manifest expects (1, {frames}, {width}).");
            }
            throw new InvalidDataException($"RPM graph has no static input '{name}' (1, {frames}, {width}).");
        }

        public void Dispose()
        {
            _pendingMotion = _pendingRotations = _pendingMotionCtx = _pendingPrevPred = null; // owned by the worker
            _tSparse?.Dispose();
            _tMotion?.Dispose();
            _tPrev?.Dispose();
            _tJoints?.Dispose();
            _worker?.Dispose();
        }
    }
}
