using System;
using UnityEngine;

namespace VrChat.Ik
{
    /// <summary>
    /// How an upper body leans and bends so the head reaches a point while the thighs stay still: a seated body on its
    /// seat, or a standing one on its legs (<see cref="IkBody"/>). Weighted shares, not an iterative chain solver: each
    /// joint takes a fixed share of each turn, most low down, as a body leans from the hips and the lower back.
    ///
    /// <para>Why not cyclic coordinate descent over the spine: it bent the neck 20–70°, against a few degrees per spine
    /// joint, and converged slowly on a head coming nearer the hips. It was moved here from VR-ChatBot's
    /// <c>SeatedPose.Lean</c> (2026-10-27), where it was first tested.</para>
    /// </summary>
    public static class SpineLean
    {
        // The share of each lean the pelvis takes, tipping forward or back over the hips (only that part of the lean:
        // a pelvis rolling sideways would lift a thigh), then spine1, spine2, spine3 and the neck: most low down, as a
        // body leans from the hips and the lower back. The spine's shares sum to 1 less the pelvis's.
        const float PelvisShare = 0.3f;
        static readonly float[] SpineShares = { 0.25f, 0.2f, 0.15f, 0.1f };
        // How a forward curl is shared over spine1, spine2, spine3 and the neck (sums to 1): the neck less, so the
        // head comes closer by a rounded back more than a hunched neck.
        static readonly float[] CurlShares = { 2f / 7f, 2f / 7f, 2f / 7f, 1f / 7f };
        // The most the spine curls forward, and arches back, from the pose it was given (degrees in all): a target
        // nearer or further than that stays out of reach.
        const float MaxCurl = 90f;
        const float MaxArch = 15f;
        // Curl, then lean, until the head is this close to the target (m), for at most this many rounds; each lean
        // takes this many passes.
        const float Tolerance = 0.001f;
        const int MaxRounds = 8;
        const int LeanPasses = 3;
        // Bisection steps of the curl: 90 degrees / 2^14, well under a millimetre at the head.
        const int CurlSteps = 14;

        /// <summary>
        /// Leans and bends an upper body so <paramref name="head"/> comes to <paramref name="target"/>, with the thighs
        /// held still. Each round, the spine curls forward or arches back about the body's left-right axis (from the
        /// left hip to the right) until the head is as far from the hips as the target is. Then the body leans towards
        /// the target about the point between the hips: the pelvis tips over the hips by a share of the lean and each
        /// spine joint turns by its own share. Rounds go on until the head is within a millimetre. A target out of
        /// reach gets the head on the line from the hips to it. A head more than about 10 cm nearer the hips than in
        /// the pose given stays short of its target.
        /// </summary>
        /// <param name="root">The transform that moves to keep the hips in place as the pelvis tips (the rig's root bone, the pelvis's parent).</param>
        /// <param name="pelvis">The pelvis, parent of both hips and of the spine.</param>
        /// <param name="leftHip">The left hip; it keeps its place and world rotation.</param>
        /// <param name="rightHip">The right hip; it keeps its place and world rotation.</param>
        /// <param name="spine">spine1, spine2, spine3 and the neck, each a descendant of the one before.</param>
        /// <param name="head">The head, below the neck: what reaches the target.</param>
        /// <param name="target">Where the head goes.</param>
        public static void Reach(Transform root, Transform pelvis, Transform leftHip, Transform rightHip, Transform[] spine,
            Transform head, Vector3 target)
        {
            var hips = (leftHip.position + rightHip.position) * 0.5f;
            var right = (rightHip.position - leftHip.position).normalized;
            Span<Quaternion> saved = stackalloc Quaternion[spine.Length];
            var curl = 0f;
            for (var round = 0; round < MaxRounds && (head.position - target).magnitude > Tolerance; round++)
            {
                curl += CurlToSpan(spine, saved, head, hips, (target - hips).magnitude, right, -MaxArch - curl, MaxCurl - curl);
                for (var pass = 0; pass < LeanPasses; pass++)
                {
                    var lean = Quaternion.FromToRotation(head.position - hips, target - hips);
                    var leftRotation = leftHip.rotation;
                    var rightRotation = rightHip.rotation;
                    pelvis.rotation = Quaternion.Slerp(Quaternion.identity, TwistAbout(lean, right), PelvisShare) * pelvis.rotation;
                    root.position += hips - (leftHip.position + rightHip.position) * 0.5f;
                    leftHip.rotation = leftRotation;
                    rightHip.rotation = rightRotation;
                    for (var i = 0; i < spine.Length; i++)
                        spine[i].rotation = Quaternion.Slerp(Quaternion.identity, lean, SpineShares[i]) * spine[i].rotation;
                }
            }
        }

        // The part of a rotation about a unit axis: the twist of its swing-twist decomposition.
        static Quaternion TwistAbout(Quaternion rotation, Vector3 axis)
        {
            var along = Vector3.Dot(new Vector3(rotation.x, rotation.y, rotation.z), axis) * axis;
            var twist = new Vector4(along.x, along.y, along.z, rotation.w);
            if (twist.sqrMagnitude < 1e-12f) return Quaternion.identity;
            twist.Normalize();
            return new Quaternion(twist.x, twist.y, twist.z, twist.w);
        }

        // Curls the spine forward (positive degrees) or arches it back about the right axis from its pose now, by the
        // angle between least and most that puts the head span from the hips, or the end that comes closest.
        // Returns the angle. Bisection, on the side the head must go: forward of its pose the head comes nearer as
        // the curl grows, and arching back takes it further while the spine is still bent forward.
        static float CurlToSpan(Transform[] spine, Span<Quaternion> saved, Transform head, Vector3 hips, float span,
            Vector3 right, float least, float most)
        {
            for (var i = 0; i < spine.Length; i++) saved[i] = spine[i].localRotation;
            var now = (head.position - hips).magnitude;
            if (now == span) return 0f;
            // From far (lo) to near (hi).
            float lo = now > span ? 0f : least, hi = now > span ? most : 0f, angle;
            if (Span(spine, saved, head, hips, lo, right) <= span) angle = lo;
            else if (Span(spine, saved, head, hips, hi, right) >= span) angle = hi;
            else
            {
                for (var step = 0; step < CurlSteps; step++)
                {
                    var mid = (lo + hi) * 0.5f;
                    if (Span(spine, saved, head, hips, mid, right) > span) lo = mid;
                    else hi = mid;
                }
                angle = (lo + hi) * 0.5f;
            }
            Span(spine, saved, head, hips, angle, right);
            return angle;
        }

        // Puts the spine back to saved, curls it by degrees in all, and returns how far the head is from the hips.
        static float Span(Transform[] spine, ReadOnlySpan<Quaternion> saved, Transform head, Vector3 hips, float degrees,
            Vector3 right)
        {
            for (var i = 0; i < spine.Length; i++) spine[i].localRotation = saved[i];
            // Positive about the right axis turns up towards forward: a forward curl.
            for (var i = 0; i < spine.Length; i++)
                spine[i].rotation = Quaternion.AngleAxis(degrees * CurlShares[i], right) * spine[i].rotation;
            return (head.position - hips).magnitude;
        }
    }
}
