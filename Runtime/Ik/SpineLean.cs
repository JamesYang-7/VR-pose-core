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
        // Curl, then lean, for this many rounds, every call: the head then ends within a few millimetres of a reachable
        // target. Each lean takes this many passes. Only a head already this close to the target (m) skips the rest.
        // Why a fixed count, not "until within 1 mm" as until 2026-11-12: the body is re-posed from the worker's pose
        // every frame, and where the rounds stop is where the spine ends. Stopping at a tolerance made the result jump
        // whenever one more round was needed: as a target drifted smoothly the spine stood still, then turned up to
        // 0.2 degrees at every joint in one frame, ten times its steady step. Rounds until within 0.01 mm were as
        // smooth but cost twice as much.
        const int Rounds = 5;
        const float Tolerance = 1e-6f;
        const int LeanPasses = 3;
        // What a degree of curl away from the given pose costs against the head's distance from the hips missing its
        // target (m^2 per degree^2): 1 mm of distance is worth 10 degrees of curl. Why damped, not the distance
        // matched exactly as until 2026-11-12: near a straight spine curling hardly changes that distance (5 degrees,
        // ~1 mm), so matching it swung the curl by degrees a frame for a head moving a tenth of a millimetre, and the
        // lean swung the pelvis with it: 2-3 degrees in one frame on a seated body sitting up tall or slouching, which
        // threw the shoulders 2-3 cm. Damped, the head may miss by a few millimetres there instead.
        const float CurlDamping = 1e-8f;
        // Golden-section steps for the span's peak (105 degrees * 0.618^14, 0.13 degrees) and for the damped curl
        // (0.618^18, 0.02 degrees).
        const int PeakSteps = 14;
        const int CurlSteps = 18;
        const float GoldenRatio = 0.618034f;

        /// <summary>
        /// Leans and bends an upper body so <paramref name="head"/> comes to <paramref name="target"/>, with the thighs
        /// held still. Each round, the spine curls forward, or straightens back, about the body's left-right axis (from
        /// the left hip to the right) to bring the head about as far from the hips as the target is, damped: it bends
        /// away from the given pose only as far as that pays for itself. Then the body leans towards the target about
        /// the point between the hips: the pelvis tips over the hips by a share of the lean and each spine joint turns
        /// by its own share. Five rounds, every call, bring the head within a few millimetres, a result that changes
        /// smoothly with the pose and the target from one frame to the next. A target out of reach gets the head on
        /// the line from the hips to it. A head more than about 10 cm nearer the hips than in the pose given stays
        /// short of its target.
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
            for (var round = 0; round < Rounds && (head.position - target).magnitude > Tolerance; round++)
            {
                curl += CurlToSpan(spine, saved, head, hips, (target - hips).magnitude, right, -MaxArch - curl, MaxCurl - curl, curl);
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

        // Curls the spine forward (positive degrees) or straightens it back about the right axis from its pose now, by
        // the angle between least and most that best trades the head's distance from the hips missing span against the
        // total curl away from the given pose (curled so far plus this angle): it minimises
        // (distance - span)^2 + CurlDamping * total^2. Returns the angle.
        //
        // Only on the forward side of the distance's peak. The distance peaks where the spine is straightest, and
        // arching back past that brings the head nearer again. Searching both sides (bisection, until 2026-11-12) gave
        // two answers near the peak, a slight curl and a full arch back, and flipped between them from frame to frame.
        // On the forward side the cost has one minimum, which moves smoothly with the target.
        static float CurlToSpan(Transform[] spine, Span<Quaternion> saved, Transform head, Vector3 hips, float span,
            Vector3 right, float least, float most, float curled)
        {
            for (var i = 0; i < spine.Length; i++) saved[i] = spine[i].localRotation;

            // The peak: golden-section search for the largest distance.
            float a = least, b = most;
            float x1 = b - GoldenRatio * (b - a), x2 = a + GoldenRatio * (b - a);
            float f1 = Span(spine, saved, head, hips, x1, right), f2 = Span(spine, saved, head, hips, x2, right);
            for (var step = 0; step < PeakSteps; step++)
            {
                if (f1 < f2) { a = x1; x1 = x2; f1 = f2; x2 = a + GoldenRatio * (b - a); f2 = Span(spine, saved, head, hips, x2, right); }
                else { b = x2; x2 = x1; f2 = f1; x1 = b - GoldenRatio * (b - a); f1 = Span(spine, saved, head, hips, x1, right); }
            }

            // From the peak forward: golden-section search for the least cost.
            a = (a + b) * 0.5f;
            b = most;
            x1 = b - GoldenRatio * (b - a);
            x2 = a + GoldenRatio * (b - a);
            f1 = Cost(spine, saved, head, hips, x1, right, span, curled);
            f2 = Cost(spine, saved, head, hips, x2, right, span, curled);
            for (var step = 0; step < CurlSteps; step++)
            {
                if (f1 > f2) { a = x1; x1 = x2; f1 = f2; x2 = a + GoldenRatio * (b - a); f2 = Cost(spine, saved, head, hips, x2, right, span, curled); }
                else { b = x2; x2 = x1; f2 = f1; x1 = b - GoldenRatio * (b - a); f1 = Cost(spine, saved, head, hips, x1, right, span, curled); }
            }
            var angle = (a + b) * 0.5f;
            Span(spine, saved, head, hips, angle, right);
            return angle;
        }

        // The damped curl's cost of curling by degrees more, with curled degrees already taken.
        static float Cost(Transform[] spine, ReadOnlySpan<Quaternion> saved, Transform head, Vector3 hips, float degrees,
            Vector3 right, float span, float curled)
        {
            var miss = Span(spine, saved, head, hips, degrees, right) - span;
            var total = curled + degrees;
            return miss * miss + CurlDamping * total * total;
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
