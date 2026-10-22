using System.IO;
using UnityEngine;

namespace VrChat
{
    /// <summary>
    /// One reference sequence of a model export (RPM-README §6, layout in <see cref="ModelExport"/>): a GORP test
    /// capture from <c>data/gorp/</c> (raw device stream, mocap ground truth, rest skeleton) together with one
    /// model version's <c>output_ref/</c> file for it (processed model input, the model's own rollout). Every
    /// array is float32; T model frames at 30 fps.
    /// </summary>
    public sealed class GorpSequence
    {
        /// <summary>Model frame rate of the sequences and of the step graph.</summary>
        public const float Fps = 30f;

        /// <summary>Raw device stream rows per frame: head, left controller, right controller.</summary>
        public const int RawPoints = 3;

        /// <summary>Floats per raw device pose: px py pz qw qx qy qz valid.</summary>
        public const int RawWidth = 8;

        /// <summary>Sequence name: the capture's file name without extension.</summary>
        public string Name { get; }

        /// <summary>Number of model frames T.</summary>
        public int FrameCount { get; }

        /// <summary>Raw GORP device stream, flat (T+1) × 3 × 8, GORP's Y-up frame (RPM-README §4.1).</summary>
        public float[] SparseRaw { get; }

        /// <summary>Processed model input frames, flat T × 54 (RPM-README §2).</summary>
        public float[] SparseCtx { get; }

        /// <summary>Mocap ground truth motion rows, flat T × 198 (local 6D, root global, then world joint positions).</summary>
        public float[] GroundTruth { get; }

        /// <summary>The model's rollout seeded with <c>GroundTruth[0]</c>, flat T × 198 — what the evaluation scored.</summary>
        public float[] ReferencePrediction { get; }

        /// <summary>The subject's SMPL-X rest skeleton, flat 22 × 3, input to the step graph.</summary>
        public float[] JointsRest { get; }

        GorpSequence(string name, int frames, float[] raw, float[] sparse, float[] gt, float[] pred, float[] joints)
        {
            Name = name;
            FrameCount = frames;
            SparseRaw = raw;
            SparseCtx = sparse;
            GroundTruth = gt;
            ReferencePrediction = pred;
            JointsRest = joints;
        }

        /// <summary>
        /// Loads and validates a capture (<paramref name="dataPath"/>, in <c>data/gorp/</c>) with a model version's
        /// reference output for it (<paramref name="outputRefPath"/>, in that version's <c>output_ref/</c>).
        /// </summary>
        /// <exception cref="InvalidDataException">A member is missing or has the wrong shape.</exception>
        public static GorpSequence Load(string dataPath, string outputRefPath)
        {
            var data = NpzArchive.Load(dataPath);
            var gt = Require(data, "gt_motion", PoseLayout.MotionWidth);
            int frames = gt.Shape[0];
            var raw = Member(data, "sparse_raw");
            if (raw.Shape.Length != 3 || raw.Shape[0] != frames + 1 || raw.Shape[1] != RawPoints || raw.Shape[2] != RawWidth)
                throw Invalid(data, $"'sparse_raw' has shape {raw.ShapeString()}, expected ({frames + 1}, {RawPoints}, {RawWidth})");
            var joints = Member(data, "joints_rest");
            if (joints.Data.Length != PoseLayout.Joints * 3)
                throw Invalid(data, $"'joints_rest' has shape {joints.ShapeString()}, expected ({PoseLayout.Joints}, 3)");

            var outputRef = NpzArchive.Load(outputRefPath);
            var sparse = Require(outputRef, "sparse_ctx", PoseLayout.InputWidth, frames);
            var pred = Require(outputRef, "ref_pred_motion", PoseLayout.MotionWidth, frames);

            return new GorpSequence(Path.GetFileNameWithoutExtension(dataPath), frames, raw.Data, sparse.Data, gt.Data, pred.Data, joints.Data);
        }

        /// <summary>World position (SMPL Z-up, metres) of <paramref name="joint"/> in a flat T × 198 motion array.</summary>
        public static Vector3 JointPosition(float[] motion, int frame, int joint)
        {
            int o = frame * PoseLayout.MotionWidth + PoseLayout.RotationWidth + joint * 3;
            return new Vector3(motion[o], motion[o + 1], motion[o + 2]);
        }

        /// <summary>
        /// Unity-space offset that moves this capture's first ground-truth pelvis to the horizontal origin
        /// (height kept), so a sequence plays around wherever its avatar stands rather than where GORP's
        /// subject stood. Shared by every body showing the sequence, so they stay comparable.
        /// </summary>
        public Vector3 RecenterOffset
        {
            get
            {
                var pelvis = GorpPose.ToUnity(JointPosition(GroundTruth, 0, 0));
                return new Vector3(-pelvis.x, 0f, -pelvis.z);
            }
        }

        /// <summary>
        /// Tracked position (SMPL Z-up) of input point <paramref name="point"/> (0 head, 1 left wrist,
        /// 2 right wrist) at <paramref name="frame"/>, from the processed input (RPM-README §2, [36:45]).
        /// </summary>
        public Vector3 TrackedPosition(int frame, int point)
        {
            int o = frame * PoseLayout.InputWidth + 36 + point * 3;
            return new Vector3(SparseCtx[o], SparseCtx[o + 1], SparseCtx[o + 2]);
        }

        static NpyArray Member(NpzArchive npz, string name)
        {
            if (!npz.Contains(name)) throw Invalid(npz, $"missing member '{name}'");
            return npz[name];
        }

        static NpyArray Require(NpzArchive npz, string name, int width, int frames = -1)
        {
            var a = Member(npz, name);
            if (a.Shape.Length != 2 || a.Shape[1] != width || (frames >= 0 && a.Shape[0] != frames))
                throw Invalid(npz, $"'{name}' has shape {a.ShapeString()}, expected ({(frames >= 0 ? frames.ToString() : "T")}, {width})");
            return a;
        }

        static InvalidDataException Invalid(NpzArchive npz, string what) =>
            new($"GORP sequence '{npz.Path}': {what}. See Models/rpm/RPM-README.md §6.");
    }
}
