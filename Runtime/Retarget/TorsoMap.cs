using UnityEngine;

namespace VrChat.Retarget
{
    /// <summary>
    /// Moves a point near one body (a wrist) to the same place on a body of other proportions, along each body's
    /// hips → eyes line. A point at the source's hip height keeps its offset from the hips, and one at eye height keeps
    /// its offset from the eyes. In between, the offset blends by a smoothstep of the point's height along the line.
    /// Past either end, it keeps that end's offset. A hand on the lap therefore stays on the lap and a hand at the chin
    /// stays at the chin, whatever the two torsos' lengths.
    ///
    /// <para>Smoothstep, not linear: a hand on the lap sits ~12% of the way up the torso, so a linear blend gave it 12%
    /// of the torso difference. On the CC5 Ariana, whose torso is 15.6 cm shorter than SMPL-X's, her wrist then sat
    /// 2.6 cm below her thigh's top, against 1.1 cm with the smoothstep (SMPL-X's: 0.7 cm above), and a hand at the
    /// chin 2.0 cm high on her face, against 0.7 cm. A smoothstep lets a point near either end follow that end, and
    /// still splits the difference at mid-torso.</para>
    /// </summary>
    public static class TorsoMap
    {
        /// <summary>
        /// <paramref name="point"/>, given near the source body, moved onto the target body. All positions are in the
        /// same space; the bodies are posed alike, so their hips → eyes lines are about parallel.
        /// </summary>
        public static Vector3 Point(Vector3 point, Vector3 sourceHips, Vector3 sourceEyes, Vector3 targetHips, Vector3 targetEyes)
        {
            var sourceTorso = sourceEyes - sourceHips;
            var lengthSquared = sourceTorso.sqrMagnitude;
            // A source with its eyes on its hips has no line to measure along: keep the offset from the hips.
            float along = lengthSquared > 1e-8f ? Vector3.Dot(point - sourceHips, sourceTorso) / lengthSquared : 0f;
            float eyeShare = Mathf.SmoothStep(0f, 1f, along); // clamps along to [0, 1] first
            return targetHips + (point - sourceHips) + eyeShare * (targetEyes - targetHips - sourceTorso);
        }
    }
}
