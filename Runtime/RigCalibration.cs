using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VrChat
{
    /// <summary>
    /// The geometry behind <see cref="RigCalibrator"/>: the headset height at which the pose model reconstructs a
    /// character standing in its T-pose with its soles on the floor, whether a headset and two controllers
    /// look like a wearer at rest or in a T-pose, and the turn that makes the wearer face a target.
    ///
    /// <para>The pose model reads absolute height (floor at 0) and puts the head joint where the headset says
    /// (<see cref="GorpDevice.ToSmpl"/>). With the headset higher than the character's standing head, the
    /// body can only float; with it a little lower, the body bends its knees a little instead. So the
    /// target is the character's rest-pose (T-pose) head joint above its lowest vertex, plus the headset's
    /// rise over that joint.</para>
    /// </summary>
    public static class RigCalibration
    {
        const int kHead = 15;

        // T-pose test thresholds. For scale, the SMPL-X character in its T-pose has its wrists 1.42 m
        // apart and 0.29 m below its eyes.
        // Headset tilt from upright (degrees): the wearer looks ahead.
        const float MaxHeadTilt = 30f;
        // Height difference between the two controllers (m): arms level.
        const float MaxHandLevelDifference = 0.15f;
        // Controllers' mean height below the headset (m): from slightly above it to about 25° below level arms.
        const float MinHandDrop = -0.05f;
        const float MaxHandDrop = 0.5f;
        // Horizontal distance between the controllers (m): arms spread out.
        const float MinHandSpan = 1.0f;
        // Horizontal distance of the headset from the hand-to-hand line (m), and how far along it from the
        // middle (as a fraction of the span): the head between the hands.
        const float MaxHeadOffLine = 0.3f;
        const float MaxHeadOffCentre = 0.25f;

        // At-rest test thresholds (a wearer standing with the controllers in lowered hands).
        // Each controller's height below the headset (m): from hands at the chest to hands hanging at the hips.
        const float MinHandBelowHead = 0.25f;
        const float MaxHandBelowHead = 1.1f;
        // Each controller's horizontal distance from the headset (m): within reach.
        const float MaxHandReach = 0.8f;

        /// <summary>
        /// How far an upright, level headset sits above the head joint the model derives from it (m): the vertical
        /// part of GORP's headset-to-head offset (RPM-README §4.2).
        /// </summary>
        public static float HeadsetAboveHeadJoint()
        {
            var raw = new float[GorpDevice.RawWidth];
            GorpDevice.FromUnity(Vector3.up, Quaternion.identity, 1f, raw, 0);
            var position = new double[3];
            GorpDevice.ToSmpl(raw, 0, GorpDevice.Head, position, new double[9]);
            return 1f - (float)position[2]; // SMPL world is Z-up
        }

        /// <summary>
        /// Headset height (m above the floor) at which the pose model reconstructs the character with
        /// <paramref name="bones"/> (the 22 SMPL bones, <see cref="MyUtils.AnimUtils.BindBones"/>) standing in
        /// its rest T-pose with its soles on the floor. Measured with joints 1–21 at rest (rig-local identity,
        /// as <see cref="GorpPose.RestJoints"/>), along the pelvis's up axis, from the lowest vertex of
        /// <paramref name="meshes"/>. The bones' pose is restored.
        /// </summary>
        public static float TPoseHeadsetHeight(Transform[] bones, SkinnedMeshRenderer[] meshes)
        {
            if (meshes.Length == 0) throw new ArgumentException("No skinned mesh to find the soles on.", nameof(meshes));
            var saved = new Quaternion[bones.Length];
            for (int j = 0; j < bones.Length; j++) saved[j] = bones[j].localRotation;
            for (int j = 1; j < PoseLayout.Joints; j++) bones[j].localRotation = Quaternion.identity;

            var baked = new Mesh();
            try
            {
                var up = bones[0].up;
                var sole = float.PositiveInfinity;
                foreach (var renderer in meshes)
                {
                    // Baked with its transform's scale, so the vertices need only its position and rotation.
                    renderer.BakeMesh(baked, useScale: true);
                    var t = renderer.transform;
                    foreach (var v in baked.vertices)
                        sole = Mathf.Min(sole, Vector3.Dot(t.position + t.rotation * v, up));
                }
                return Vector3.Dot(bones[kHead].position, up) - sole + HeadsetAboveHeadJoint();
            }
            finally
            {
                for (int j = 0; j < bones.Length; j++) bones[j].localRotation = saved[j];
                // Why not Destroy: this also runs in edit mode (tests, editor tools), where only DestroyImmediate works.
                Object.DestroyImmediate(baked);
            }
        }

        /// <summary>Whether a headset with up axis <paramref name="headUp"/> (world, +Y up) is looking roughly ahead.</summary>
        public static bool IsHeadLevel(Vector3 headUp) => Vector3.Angle(headUp, Vector3.up) <= MaxHeadTilt;

        /// <summary>
        /// Whether a headset (position and up axis) and two controllers, all in world space with +Y up, look like
        /// a wearer at rest: head level, both controllers held lower near the body. A headset on a desk or in
        /// the hands fails (the controllers are not below it), as does an untracked rig (everything on the
        /// floor). Uses no absolute height, so it works before the height is calibrated.
        /// </summary>
        public static bool IsAtRest(Vector3 head, Vector3 headUp, Vector3 left, Vector3 right) =>
            IsHeadLevel(headUp) && IsHandAtRest(head, left) && IsHandAtRest(head, right);

        static bool IsHandAtRest(Vector3 head, Vector3 hand)
        {
            var below = head.y - hand.y;
            return below >= MinHandBelowHead && below <= MaxHandBelowHead && Flat(hand - head).magnitude <= MaxHandReach;
        }

        /// <summary>
        /// What keeps a headset (position and up axis) and two controllers, all in world space with +Y up,
        /// from forming a T-pose: head upright, arms level and spread to the sides at about shoulder height,
        /// head between the hands. <see cref="TPoseMisses.None"/> is a T-pose. Uses no absolute height, so
        /// it works before the height is calibrated.
        /// </summary>
        public static TPoseMisses CheckTPose(Vector3 head, Vector3 headUp, Vector3 left, Vector3 right)
        {
            var misses = TPoseMisses.None;
            if (!IsHeadLevel(headUp)) misses |= TPoseMisses.HeadTilted;
            if (Mathf.Abs(left.y - right.y) > MaxHandLevelDifference) misses |= TPoseMisses.ArmsNotLevel;
            var drop = head.y - (left.y + right.y) * 0.5f;
            if (drop < MinHandDrop || drop > MaxHandDrop) misses |= TPoseMisses.HandsNotAtShoulders;

            var span = Flat(right - left);
            if (span.magnitude < MinHandSpan) return misses | TPoseMisses.HandsTooClose;
            var toHead = Flat(head - left);
            var along = Vector3.Dot(toHead, span) / span.sqrMagnitude;
            if (Mathf.Abs(along - 0.5f) > MaxHeadOffCentre || (toHead - along * span).magnitude > MaxHeadOffLine)
                misses |= TPoseMisses.HeadNotBetweenHands;
            return misses;
        }

        /// <summary>
        /// Turns <paramref name="origin"/> about the vertical through <paramref name="head"/> (a descendant)
        /// so the headset's heading points at <paramref name="target"/>; the head stays where it is.
        /// Returns the turn in degrees (positive: clockwise seen from above); 0 when the target is straight
        /// above or below the head or the head looks straight up or down.
        /// </summary>
        public static float TurnToFace(Transform origin, Transform head, Vector3 target)
        {
            var heading = Flat(head.forward);
            var toTarget = Flat(target - head.position);
            if (heading.sqrMagnitude < 1e-8f || toTarget.sqrMagnitude < 1e-8f) return 0f;
            var turn = Vector3.SignedAngle(heading, toTarget, Vector3.up);
            origin.RotateAround(head.position, Vector3.up, turn);
            return turn;
        }

        static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
    }
}
