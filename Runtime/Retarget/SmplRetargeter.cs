using System;
using UnityEngine;

namespace VrChat.Retarget
{
    /// <summary>
    /// Copies the 22-joint SMPL pose, and the fingers when both rigs have them, from the SMPL avatar
    /// (posed by a live worker locally, or by <c>NetworkedPoseSync</c> for a remote) onto any
    /// Humanoid-rigged character, e.g. a Character Creator 5 export, every frame. The SMPL avatar stays the pose source and the network
    /// format; this is a render-side skin over it. How the pose is matched: <see cref="SmplRetarget"/>.
    ///
    /// <para>Runs in <see cref="Application.onBeforeRender"/> so it follows the source's LateUpdate and
    /// any Animator on the target, like <c>MirrorAvatar</c>. Binds and calibrates in Start, which
    /// requires the target to still be in its bind pose (a T-pose).</para>
    /// </summary>
    public sealed class SmplRetargeter : MonoBehaviour
    {
        /// <summary>Root of the SMPL avatar whose pose is copied.</summary>
        [field: SerializeField]
        [field: Tooltip("Root of the SMPL avatar whose pose is copied (the one a live worker or the network sync drives).")]
        public Transform Source { get; set; }

        /// <summary>The Humanoid-rigged character that receives the pose.</summary>
        [field: SerializeField]
        [field: Tooltip("Humanoid-rigged character that receives the pose; its Avatar only supplies the bone mapping.")]
        public Animator Target { get; set; }

        /// <summary>Hide the SMPL avatar's meshes once the target takes over.</summary>
        [field: SerializeField]
        [field: Tooltip("Disable the SMPL avatar's renderers so only the target shows. Turn off to compare side by side.")]
        public bool HideSourceMeshes { get; set; } = true;

        /// <summary>Place the target so its eyes land on the source's eyes.</summary>
        [field: SerializeField]
        [field: Tooltip("Move the target's hips so its eyes land on the source's eyes (the headset). Falls back to head joint on head joint if a rig has no eye bones. Off = target stays where its own transforms put it.")]
        public bool AnchorEyes { get; set; } = true;

        /// <summary>Limbs re-aimed along SMPL's rest directions (read in Start).</summary>
        [field: SerializeField]
        [field: Tooltip("Arms: swing each arm at the shoulder so its shoulder-wrist line points where SMPL's does at rest; elbow and wrist angles stay copied. Clavicles: turn each clavicle about the vertical to SMPL's heading, moving the shoulder joint forward to where SMPL's sits. Read in Start.")]
        public LimbAims Aims { get; set; } = LimbAims.Arms | LimbAims.Clavicles;

        /// <summary>Pin the target wrists to the source wrists with two-bone IK.</summary>
        [field: SerializeField]
        [field: Tooltip("Two-bone arm IK so the target's wrists reach the source's (controller-pinned) wrists despite different arm lengths. Bends the arm away from the source's joint angles; off keeps angles exact.")]
        public bool PinWrists { get; set; }

        /// <summary>Share limb roll along the target's twist bones.</summary>
        [field: SerializeField]
        [field: Tooltip("Spread each limb's roll along its twist bones (bones named *Twist*) so elbows and wrists don't pinch.")]
        public bool DistributeTwist { get; set; } = true;

        /// <summary>World offset from the source placement.</summary>
        [field: SerializeField]
        [field: Tooltip("World offset added to the eye anchor and wrist goals — e.g. (1,0,0) stands the target beside the source for comparison.")]
        public Vector3 Offset { get; set; }

        SmplRetarget _retarget;

        void Start()
        {
            try
            {
                _retarget = new SmplRetarget(Source, Target, Aims);
            }
            catch (ArgumentException e)
            {
                Debug.LogError($"[SmplRetargeter] {e.Message}", this);
                enabled = false;
                return;
            }

            if (HideSourceMeshes)
            {
                foreach (var r in Source.GetComponentsInChildren<Renderer>()) r.enabled = false;
            }
            Debug.Log($"[SmplRetargeter] {Target.name}: {_retarget.JointCount} joints ({_retarget.AimedJointCount} limb bones aimed), " +
                      $"{_retarget.TwistSegmentCount} twist segments, anchored by {(_retarget.AnchorsEyes ? "eyes" : "head joint")}.", this);
        }

        void OnEnable() => Application.onBeforeRender += Retarget;
        void OnDisable() => Application.onBeforeRender -= Retarget;

        void Retarget()
        {
            if (_retarget == null) return; // before Start
            _retarget.PinWrists = PinWrists;
            _retarget.DistributeTwist = DistributeTwist;
            _retarget.Anchor = AnchorEyes;
            _retarget.Offset = Offset;
            _retarget.Apply();
        }
    }
}
