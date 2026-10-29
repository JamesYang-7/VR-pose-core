using UnityEngine;

namespace VrChat.Ik
{
    /// <summary>
    /// Two-bone IK solved in closed form (law of cosines), as engines solve a limb: a root joint (a shoulder), a middle
    /// joint (an elbow) and an end (a wrist). The end goes on a target, or as near as the bone lengths allow, and the
    /// middle joint bends to the side a hint gives.
    ///
    /// <para>Why not cyclic coordinate descent (<c>AnimUtils.SolveTwoBoneIK</c>): it keeps whatever bend the pose it
    /// starts from has, so it needs a good pose to start from (a pose model's). This one puts the middle joint where
    /// the hint says on every solve, whatever the pose before.</para>
    ///
    /// <para><see cref="Solve"/> turns the root so the middle joint lands where <see cref="Place"/> puts it, with the
    /// limb's flexing side turned to face across the bend, then turns the middle joint into the bend. So the limb
    /// bends the way it flexes at rest, rather than about whatever axis the shortest turn would pick, which would
    /// twist the upper bone.</para>
    /// </summary>
    public static class TwoBoneIk
    {
        // The reach stays this share of the bones' length short of straight: at full stretch the bend has no side,
        // and the middle joint would snap to one as the target crosses it.
        const float MaxStretch = 0.999f;
        // How much the last solve's bend side counts against the hint's: enough to carry the bend through a hint
        // that lines up with the limb, too little to pull it away from the hint otherwise.
        const float BendMemory = 0.1f;

        /// <summary>
        /// Where the middle joint and the end go. The end goes on <paramref name="target"/>, or on the line from
        /// <paramref name="root"/> to it as far as the bones reach (and no nearer than the difference of their
        /// lengths). The middle joint goes off that line towards <paramref name="bend"/>, the part of it at right
        /// angles to the line (any right angle when <paramref name="bend"/> lies along it).
        /// </summary>
        public static (Vector3 mid, Vector3 end) Place(Vector3 root, Vector3 target, float upperLength, float lowerLength,
            Vector3 bend)
        {
            var along = Along(root, target);
            var side = Across(bend, along);
            var (reach, cos, sin) = Triangle(root, target, upperLength, lowerLength);
            return (root + upperLength * (cos * along + sin * side), root + reach * along);
        }

        /// <summary>
        /// Turns <paramref name="upper"/> and <paramref name="mid"/> (world rotations) so <paramref name="end"/> lands
        /// where <see cref="Place"/> puts it, bent towards <paramref name="bendHint"/>.
        /// </summary>
        /// <param name="upper">The root joint (a shoulder).</param>
        /// <param name="mid">The middle joint (an elbow), a child of <paramref name="upper"/>.</param>
        /// <param name="end">The end (a wrist), a child of <paramref name="mid"/>.</param>
        /// <param name="target">Where the end goes.</param>
        /// <param name="bendHint">The side of the line from the root to the target the middle joint goes to.</param>
        /// <param name="flex">The way the lower bone swings away from the upper's line as the middle joint bends, in the pose now (an elbow from a T-pose: forward).</param>
        /// <param name="bend">The bend side of the last solve, which carries the bend through a hint along the limb (zero the first time); this solve's is written back.</param>
        public static void Solve(Transform upper, Transform mid, Transform end, Vector3 target, Vector3 bendHint,
            Vector3 flex, ref Vector3 bend)
        {
            var root = upper.position;
            var upperLength = (mid.position - root).magnitude;
            var lowerLength = (end.position - mid.position).magnitude;
            var along = Along(root, target);
            // The hint's part across the limb shrinks as it lines up with it, and the last bend takes over.
            var hintAcross = bendHint - Vector3.Dot(bendHint, along) * along;
            var side = Across(hintAcross + BendMemory * bendHint.magnitude * bend, along);
            bend = side;
            var (reach, cos, sin) = Triangle(root, target, upperLength, lowerLength);

            // The upper bone's direction and its flexing side, now and solved. In the bend plane the lower bone leaves
            // the upper's line away from the bend side: (sin, -cos) in (along, side), defined even with the limb straight.
            var direction = (mid.position - root).normalized;
            var solved = cos * along + sin * side;
            var turn = Quaternion.LookRotation(solved, sin * along - cos * side)
                       * Quaternion.Inverse(Quaternion.LookRotation(direction, Across(flex, direction)));
            upper.rotation = turn * upper.rotation;

            var endTarget = root + reach * along;
            mid.rotation = Quaternion.FromToRotation(end.position - mid.position, endTarget - mid.position) * mid.rotation;
        }

        // The unit line from the root to the target; straight down for a target on the root.
        static Vector3 Along(Vector3 root, Vector3 target)
        {
            var toTarget = target - root;
            var distance = toTarget.magnitude;
            return distance > 1e-6f ? toTarget / distance : Vector3.down;
        }

        // The unit part of v at right angles to the unit axis; when v lies along it, any right angle.
        static Vector3 Across(Vector3 v, Vector3 axis)
        {
            var across = v - Vector3.Dot(v, axis) * axis;
            if (across.sqrMagnitude > 1e-10f) return across.normalized;
            across = Vector3.Cross(axis, Vector3.up);
            if (across.sqrMagnitude < 1e-6f) across = Vector3.Cross(axis, Vector3.right);
            return across.normalized;
        }

        // The reach (root to end) and the root's angle between the reach and the upper bone, as cosine and sine.
        static (float reach, float cos, float sin) Triangle(Vector3 root, Vector3 target, float upperLength, float lowerLength)
        {
            var reach = Mathf.Clamp((target - root).magnitude, Mathf.Abs(upperLength - lowerLength),
                (upperLength + lowerLength) * MaxStretch);
            reach = Mathf.Max(reach, 1e-4f); // equal bones and a target on the root: fold the limb in two
            var cos = Mathf.Clamp((upperLength * upperLength + reach * reach - lowerLength * lowerLength)
                                  / (2f * upperLength * reach), -1f, 1f);
            return (reach, cos, Mathf.Sqrt(1f - cos * cos));
        }
    }
}
