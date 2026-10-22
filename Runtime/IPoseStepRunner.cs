using System;

namespace VrChat
{
    /// <summary>
    /// A pose model's step loop as the live pipeline drives it (e.g.
    /// <see cref="RpmStepRunner"/>): one 54-float input frame per model frame in, one committed pose out per step,
    /// for that same newest frame. Each runner seeds itself from a standing pose on the frames it is fed, and may
    /// need several frames before its first step.
    /// </summary>
    public interface IPoseStepRunner : IDisposable
    {
        /// <summary>The pose of the last step: 22 × 6D rotations (local, root global), then 22 world joint positions (SMPL Z-up).</summary>
        float[] MotionOut { get; }

        /// <summary>The pose of the last step as 22 row-major 3×3 matrices (local; root global, SMPL Z-up).</summary>
        float[] JointRotations { get; }

        /// <summary>Steps completed since the last start.</summary>
        int StepCount { get; }

        /// <summary>True between a <see cref="Begin"/> that scheduled a step and the call that collects it.</summary>
        bool IsStepPending { get; }

        /// <summary>Main-thread time of the last step, in ms: upload and scheduling, plus any blocking wait.</summary>
        double LastMainThreadMs { get; }

        /// <summary>Time from scheduling the last step to having its result, in ms.</summary>
        double LastLatencyMs { get; }

        /// <summary>Restarts the rollout from the model's live standing seed, built from the next frames fed.</summary>
        void StartStanding(float[] jointsRest);

        /// <summary>
        /// Takes the newest input frame and schedules its step without waiting. Returns false, scheduling nothing,
        /// while the model still needs more frames before its first step.
        /// </summary>
        bool Begin(float[] frame);

        /// <summary>
        /// Collects the pending step if its result has arrived; never waits for the GPU. A readback that arrived
        /// with an error is read again synchronously (<see cref="TensorReadback"/>).
        /// </summary>
        bool TryComplete();

        /// <summary>Collects the pending step, waiting for the GPU if needed. Does nothing if none is pending.</summary>
        void Complete();
    }
}
