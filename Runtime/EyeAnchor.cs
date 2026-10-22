using UnityEngine;

namespace VrChat
{
    /// <summary>
    /// Where a character's eyes sit on its head bone, read once from its two eye bones. The headset
    /// sits on the wearer's eyes, so this is what links a headset pose to a head-bone pose:
    /// <see cref="EyesPosition"/> lets a body be moved so its eyes land on the headset (the camera
    /// itself never moves), and <see cref="EyesInFrame"/> gives the eye offset in a body frame, from
    /// which a headset pose yields the head-joint pose the models were trained on.
    ///
    /// <para>Each rig puts its head joint somewhere different inside the skull (SMPL-X ~4.5 cm below
    /// the eyes, the CC5 Aaron ~8.5 cm), so anchoring two bodies head joint to head joint offsets
    /// everything below the head by the difference; anchoring both by the eyes does not.</para>
    ///
    /// <para>The offset is kept in the head bone's local axes and its parent's units, so it follows
    /// the head rotation actually rendered each frame and ignores the head bone's own scale.
    /// Why not read the eye bones live: <c>NetworkedPoseSync.HideOwnHead</c> shrinks the owner's
    /// head bone to 1e-4, which pulls the eye bones into the head joint.</para>
    /// </summary>
    public sealed class EyeAnchor
    {
        readonly Transform _head;
        readonly Vector3 _eyesInHead;

        /// <param name="head">The head bone.</param>
        /// <param name="leftEye">The left eye bone (any descendant of <paramref name="head"/>).</param>
        /// <param name="rightEye">The right eye bone (any descendant of <paramref name="head"/>).</param>
        public EyeAnchor(Transform head, Transform leftEye, Transform rightEye)
        {
            _head = head;
            // Head-local units, i.e. with the head bone's own scale divided out — the same value
            // whether or not the head is collapsed when this runs.
            _eyesInHead = head.InverseTransformPoint((leftEye.position + rightEye.position) * 0.5f);
        }

        /// <summary>World position of the midpoint between the eyes, for the head's current pose.</summary>
        public Vector3 EyesPosition => _head.position + _head.rotation * Vector3.Scale(ParentScale, _eyesInHead);

        /// <summary>
        /// Head bone → eye midpoint, expressed in <paramref name="frame"/>. With the body in a
        /// reference pose and <paramref name="frame"/> its facing (+Y up, +Z forward, as a headset's
        /// axes), a headset at pose (p, R) puts this body's head joint at <c>p − R · result</c>.
        /// </summary>
        public Vector3 EyesInFrame(Quaternion frame) => Quaternion.Inverse(frame) * (EyesPosition - _head.position);

        Vector3 ParentScale => _head.parent != null ? _head.parent.lossyScale : Vector3.one;
    }
}
