using System;
using UnityEngine;

namespace VrChat
{
    /// <summary>
    /// Device poses for the pose models (<c>Models/rpm/RPM-README.md</c> §4.1–4.2). A raw pose
    /// is GORP's recording format: 8 floats <c>[px py pz qw qx qy qz valid]</c> in its right-handed, Y-up,
    /// floor-level frame, with the rotation stored transposed (the README's "chirality" conjugate).
    /// <see cref="ToSmpl"/> is the README's conversion of one raw pose to the SMPL head / wrist joint the
    /// model reads; <see cref="FromUnity"/> writes the raw pose a Unity tracked pose stands for, and
    /// <see cref="ToUnity"/> turns a raw pose back into a Unity one (for replaying GORP captures).
    ///
    /// <para><b>Unity → raw.</b> Measured on the GORP captures against their mocap (31 subjects): the raw
    /// headset frame is the OpenXR view frame (+x right, +y up, −z forward), i.e. Unity's camera with its z
    /// flipped, and the raw world is right-handed. So a Unity pose maps as position (−x, y, z) and frame
    /// W·R·S, W = diag(−1, 1, 1), S = diag(1, 1, −1). The world's x flip (rather than z) keeps it the inverse
    /// of <see cref="GorpPose.ToUnity"/>: after the README's Y-up → Z-up turn it is
    /// <see cref="MyUtils.MathUtils.UnityToBlender"/>, so the model's output lands back where the input came
    /// from. With these, the headset becomes exactly the head joint frame of the project's Blender convention
    /// (<see cref="MyUtils.MathUtils.GlobalRotationUnityToBlender"/>).</para>
    ///
    /// <para>GORP's raw controller pose is not the OpenXR grip pose but Meta's older ("legacy") controller pose:
    /// the grip pose moved 10.1 cm along its pinky-to-thumb axis and 2.0 cm down, then turned 60° about its
    /// palm axis (<see cref="FromGrip"/> with <see cref="GripOffset"/> and <see cref="GripCorrectionEuler"/>, Meta's
    /// published grip → old aim pose transform). Measured on a Quest over Link (2026-10-21): a pivot fit of the
    /// wrist joint, with the forearm held still, lands 2.1–2.4 cm from where this transform and GORP's offset put
    /// it (the plain grip pose: 7–8 cm), and the hand's bend and twist axes give a turn of 43–58°. Callers pass the
    /// transformed pose (the live worker's grip corrections).</para>
    /// </summary>
    public static class GorpDevice
    {
        /// <summary>Raw pose index of the headset.</summary>
        public const int Head = 0;

        /// <summary>Raw pose index of the left controller.</summary>
        public const int Left = 1;

        /// <summary>Raw pose index of the right controller.</summary>
        public const int Right = 2;

        /// <summary>Floats per raw pose: px py pz qw qx qy qz valid.</summary>
        public const int RawWidth = GorpSequence.RawWidth;

        /// <summary>Raw poses per frame: head, left, right.</summary>
        public const int Points = GorpSequence.RawPoints;

        /// <summary>
        /// Where GORP's controller point sits from the OpenXR grip pose, in the grip's own Unity axes (metres): Meta's
        /// grip → old aim pose translation (OpenXR (0, −0.019641, −0.100981), z flipped). The same for both hands.
        /// </summary>
        public static readonly Vector3 GripOffset = new(0f, -0.019641f, 0.100981f);

        /// <summary>GORP's controller frame from the OpenXR grip frame, as Unity Euler angles: Meta's 60° about the palm axis.</summary>
        public static readonly Vector3 GripCorrectionEuler = new(60f, 0f, 0f);

        // README 4.2: device point → joint offsets, in the device frame after the axis fix (metres).
        static readonly double[][] kOffsets =
        {
            new[] { -0.01568759, 0.02414007, 0.09251691 },
            new[] { 0.12183987, -0.05452635, 0.06948892 },
            new[] { -0.11596493, -0.04973321, 0.06753435 },
        };

        // README 4.2 axis fixes F_d, row-major.
        static readonly double[][] kAxisFix =
        {
            new double[] { -1, 0, 0, 0, 1, 0, 0, 0, -1 },
            new double[] { 0, -1, 0, 0, 0, 1, -1, 0, 0 },
            new double[] { 0, 1, 0, 0, 0, 1, 1, 0, 0 },
        };

        /// <summary>
        /// README 4.2 for one raw pose: writes the SMPL joint position (Z-up, metres) to
        /// <paramref name="position"/>[3] and its rotation (row-major 3×3, column vectors) to
        /// <paramref name="rotation"/>[9]. Done in double, like the exporters' reference code.
        /// </summary>
        public static void ToSmpl(float[] raw, int offset, int device, double[] position, double[] rotation)
        {
            // R = Rx · Rgᵀ · F. Rx = [[1,0,0],[0,0,-1],[0,1,0]], so (Rx·M) has rows (M0, -M2, M1).
            Span<double> rg = stackalloc double[9];
            QuaternionMatrix(raw[offset + 3], raw[offset + 4], raw[offset + 5], raw[offset + 6], rg);
            Span<double> rxRgT = stackalloc double[9];
            for (int c = 0; c < 3; c++)
            {
                rxRgT[c] = rg[c * 3];          // row 0 of Rgᵀ = column 0 of Rg
                rxRgT[3 + c] = -rg[c * 3 + 2]; // -(row 2 of Rgᵀ)
                rxRgT[6 + c] = rg[c * 3 + 1];  // row 1 of Rgᵀ
            }
            var fix = kAxisFix[device];
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    rotation[r * 3 + c] = rxRgT[r * 3] * fix[c] + rxRgT[r * 3 + 1] * fix[3 + c] + rxRgT[r * 3 + 2] * fix[6 + c];

            // p = Rx · p_raw − R · offset
            double px = raw[offset], py = -(double)raw[offset + 2], pz = raw[offset + 1];
            var o = kOffsets[device];
            position[0] = px - (rotation[0] * o[0] + rotation[1] * o[1] + rotation[2] * o[2]);
            position[1] = py - (rotation[3] * o[0] + rotation[4] * o[1] + rotation[5] * o[2]);
            position[2] = pz - (rotation[6] * o[0] + rotation[7] * o[1] + rotation[8] * o[2]);
        }

        /// <summary>
        /// Writes the raw GORP pose for a Unity world pose (tracking origin on the floor) to
        /// <paramref name="raw"/>[offset .. offset+8]. For a controller, <paramref name="rotation"/> must already
        /// carry the grip correction (see the class remarks).
        /// </summary>
        public static void FromUnity(Vector3 position, Quaternion rotation, float valid, float[] raw, int offset)
        {
            raw[offset] = -position.x;
            raw[offset + 1] = position.y;
            raw[offset + 2] = position.z;

            // D = W·R·S (W negates row 0, S negates column 2); the stored rotation is Dᵀ.
            Span<double> m = stackalloc double[9];
            QuaternionMatrix(rotation.w, rotation.x, rotation.y, rotation.z, m);
            Span<double> stored = stackalloc double[9];
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    stored[c * 3 + r] = m[r * 3 + c] * (r == 0 ? -1 : 1) * (c == 2 ? -1 : 1);
            MatrixQuaternion(stored, out raw[offset + 3], out raw[offset + 4], out raw[offset + 5], out raw[offset + 6]);
            raw[offset + 7] = valid;
        }

        /// <summary>
        /// The controller pose GORP recorded for a controller whose OpenXR grip pose is <paramref name="grip"/>
        /// (Unity world): moved by <paramref name="offset"/> along the grip's own axes, then turned by
        /// <paramref name="correction"/>. Defaults: <see cref="GripOffset"/>, <see cref="GripCorrectionEuler"/>.
        /// </summary>
        public static Pose FromGrip(Pose grip, Vector3 offset, Quaternion correction) =>
            new(grip.position + grip.rotation * offset, grip.rotation * correction);

        /// <summary>The OpenXR grip pose a GORP controller pose stands for: the inverse of <see cref="FromGrip"/>.</summary>
        public static Pose ToGrip(Pose controller, Vector3 offset, Quaternion correction)
        {
            var rotation = controller.rotation * Quaternion.Inverse(correction);
            return new Pose(controller.position - rotation * offset, rotation);
        }

        /// <summary>
        /// The Unity world pose a raw GORP pose at <paramref name="raw"/>[offset] stands for: the inverse of
        /// <see cref="FromUnity"/>. For a controller the rotation still carries the grip correction. The valid
        /// flag is not read.
        /// </summary>
        public static Pose ToUnity(float[] raw, int offset)
        {
            Span<double> stored = stackalloc double[9];
            QuaternionMatrix(raw[offset + 3], raw[offset + 4], raw[offset + 5], raw[offset + 6], stored);
            Span<double> m = stackalloc double[9];
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    m[r * 3 + c] = stored[c * 3 + r] * (r == 0 ? -1 : 1) * (c == 2 ? -1 : 1);
            MatrixQuaternion(m, out float w, out float x, out float y, out float z);
            return new Pose(new Vector3(-raw[offset], raw[offset + 1], raw[offset + 2]), new Quaternion(x, y, z, w));
        }

        // README 4.2 matrix(q), s = 2 / |q|²; row-major. An all-zero quaternion (some dropped-out rows) becomes
        // the identity instead of NaN: those hands are masked downstream anyway.
        static void QuaternionMatrix(double w, double x, double y, double z, Span<double> m)
        {
            double n = w * w + x * x + y * y + z * z;
            double s = n > 1e-12 ? 2.0 / n : 0.0;
            m[0] = 1 - s * (y * y + z * z); m[1] = s * (x * y - z * w); m[2] = s * (x * z + y * w);
            m[3] = s * (x * y + z * w); m[4] = 1 - s * (x * x + z * z); m[5] = s * (y * z - x * w);
            m[6] = s * (x * z - y * w); m[7] = s * (y * z + x * w); m[8] = 1 - s * (x * x + y * y);
        }

        // Rotation matrix (row-major) → unit quaternion (w, x, y, z), w ≥ 0 (Shepperd's method).
        static void MatrixQuaternion(ReadOnlySpan<double> m, out float w, out float x, out float y, out float z)
        {
            double qw, qx, qy, qz, trace = m[0] + m[4] + m[8];
            if (trace > 0)
            {
                double s = 2 * Math.Sqrt(1 + trace);
                qw = s / 4; qx = (m[7] - m[5]) / s; qy = (m[2] - m[6]) / s; qz = (m[3] - m[1]) / s;
            }
            else if (m[0] > m[4] && m[0] > m[8])
            {
                double s = 2 * Math.Sqrt(1 + m[0] - m[4] - m[8]);
                qw = (m[7] - m[5]) / s; qx = s / 4; qy = (m[1] + m[3]) / s; qz = (m[2] + m[6]) / s;
            }
            else if (m[4] > m[8])
            {
                double s = 2 * Math.Sqrt(1 + m[4] - m[0] - m[8]);
                qw = (m[2] - m[6]) / s; qx = (m[1] + m[3]) / s; qy = s / 4; qz = (m[5] + m[7]) / s;
            }
            else
            {
                double s = 2 * Math.Sqrt(1 + m[8] - m[0] - m[4]);
                qw = (m[3] - m[1]) / s; qx = (m[2] + m[6]) / s; qy = (m[5] + m[7]) / s; qz = s / 4;
            }
            double sign = qw < 0 ? -1 : 1;
            w = (float)(sign * qw); x = (float)(sign * qx); y = (float)(sign * qy); z = (float)(sign * qz);
        }
    }
}
