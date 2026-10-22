using System;
using System.Collections.Generic;
using MyUtils;
using UnityEngine;

namespace VrChat.Retarget
{
    /// <summary>
    /// The SMPL → Humanoid retarget for one source/target pair: bound and calibrated on construction,
    /// <see cref="Apply"/> copies the source's current pose. <see cref="SmplRetargeter"/> runs it every
    /// frame; the editor's <c>RetargetFit</c> runs it frame by frame over the reference clips.
    ///
    /// <para>Direct bone mapping, not Mecanim retargeting: the Humanoid Avatar is only used to
    /// <i>find</i> the bones (<see cref="Animator.GetBoneTransform"/>); the pose goes through
    /// <see cref="RetargetSolver"/>, which matches joint <i>angles</i>: every target joint turns by
    /// the rotation its SMPL joint turns, in body axes. No muscle-space clamping, no
    /// hips-as-centre-of-mass remapping.</para>
    ///
    /// <para>Where the angles alone leave the wrists off, the arms are re-aimed (<see cref="LimbAims"/>),
    /// measured on the CC5 Aaron over the reference clips (<c>RetargetFit</c>):
    /// <list type="bullet">
    /// <item><b>Clavicles</b>, heading only: CC5's clavicle runs ~22° back from the neck to the
    /// shoulder, SMPL's collar line only 3–9°, so Aaron's shoulder joints sat 4–6 cm behind SMPL's
    /// relative to the eyes in every pose. Turning each clavicle about the vertical to SMPL's heading
    /// moves the shoulder forward without raising it (SMPL's collar line also rises ~23°; copying that
    /// gave a permanent shrug). Wrist error 5.4 → 2.4 cm mean.</item>
    /// <item><b>Arms</b>: each arm swung at the shoulder so its shoulder→wrist line points along SMPL's
    /// at rest (SMPL's zero-pose arms hang ~5° low and 1–8° back). One swing per arm, shared by the
    /// upper arm, forearm and hand, so the elbow and wrist angles are still copied exactly. Why not aim
    /// each bone along its own SMPL bone: SMPL's rest joint centres zig-zag (upper arm 17° down,
    /// forearm 5° up), so per-bone aims put a ~20° kink into the straight T-pose elbow and made the
    /// wrists worse (5.7 → 6.7 cm).</item>
    /// </list>
    /// The legs are not aimed: SMPL's hip joints sit 4 cm closer together than Aaron's, so aiming the
    /// hip→ankle lines parallel moved his ankles further from SMPL's (2.7 → 5.6 cm).</para>
    ///
    /// <para><b>Fingers</b>, when both rigs have them (SMPL-X's 15 joints per hand, the Humanoid finger
    /// bones): their rest is SMPL-X's flat hand, and each phalanx is aimed along SMPL-X's, so the target's
    /// fingers spread and its thumb sits as SMPL-X's do. The FBX's own curl (the relaxed mean hand) then
    /// carries over; on CC5's bind alone the fingers bunched and the thumb tucked under the palm. Each
    /// frame the index, ring and pinky then turn sideways at the knuckle, so the gaps between the fingers
    /// are SMPL-X's scaled to the target's knuckle span rather than the same angles from closer-set knuckles.</para>
    ///
    /// <para>Per <see cref="Apply"/>: rotations → finger spacing → (optional, off by default) two-bone IK pulling the
    /// target wrists onto the source wrists, which trades joint angles for wrist position → twist
    /// bones → move the hips so the target's eyes land on the source's eyes (a pure translation,
    /// angles untouched). Eyes, not head joints: the headset sits on the eyes, and each template puts
    /// its head joint somewhere different inside the skull (SMPL-X 4.5 cm below the eyes, the CC5
    /// Aaron 8.5 cm), so a head-joint anchor shifts the whole body by the difference. A rig without
    /// eye bones falls back to the head joint.</para>
    ///
    /// <para>Calibrates on construction, which requires the target to be in its bind pose and that
    /// bind to be a T-pose: the SMPL zero pose (the canonical T-pose) and the target bind are taken
    /// to be the same pose, joint for joint. An A-posed rig would come out with its arms 40° low.
    /// The source is posed to its rest for the calibration and restored afterwards.</para>
    /// </summary>
    public sealed class SmplRetarget
    {
        // SMPL joint (AnimUtils.boneNames order) → Humanoid bone.
        static readonly HumanBodyBones[] kHumanBone =
        {
            HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.Spine,
            HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.Chest,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.UpperChest,
            HumanBodyBones.LeftToes, HumanBodyBones.RightToes, HumanBodyBones.Neck,
            HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm,
            HumanBodyBones.RightLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
        };

        // SMPL-X finger joints (thumb, index, middle, ring, pinky; 1–3 proximal to distal) → Humanoid finger bones,
        // which HumanBodyBones lists in the same order per hand. Proximal before distal, so each phalanx's parent
        // is written first.
        static readonly string[] kSmplxFingers = { "thumb", "index", "middle", "ring", "pinky" };

        const int kIndex = 1, kMiddle = 2, kRing = 3, kPinky = 4; // in kSmplxFingers

        // Largest sideways turn a finger gets to even out the gaps, so a rig with odd knuckles can't fan a finger out.
        const float kMaxSpreadDegrees = 20f;

        const int kPelvis = 0, kLeftHip = 1, kRightHip = 2, kHead = 15;
        const int kLeftShoulder = 16, kRightShoulder = 17, kLeftElbow = 18, kRightElbow = 19, kLeftWrist = 20, kRightWrist = 21;

        readonly Transform[] _src;
        readonly List<Transform> _srcFingers = new();
        // Per hand with all four fingers: the target hand, both rigs' finger chains (kSmplxFingers order, 3 joints
        // each) and the target's knuckle span over the source's.
        readonly List<(Transform hand, Transform[][] src, Transform[][] tgt, float scale)> _spreadHands = new();
        readonly Transform[] _tgt;
        readonly RetargetSolver _solver;
        readonly List<TwistDistributor> _twists = new();
        readonly EyeAnchor _srcEyes, _tgtEyes; // both null when either rig lacks eye bones
        readonly int _leftHandJoint = -1, _rightHandJoint = -1;
        const int kLeftCollar = 13, kRightCollar = 14;

        /// <summary>Two-bone IK pulling the target wrists onto the source wrists (bends the arms away from the source's angles).</summary>
        public bool PinWrists { get; set; }

        /// <summary>Share each limb's roll along the target's twist bones.</summary>
        public bool DistributeTwist { get; set; } = true;

        /// <summary>Move the target so its eyes (or head joint, see <see cref="AnchorsEyes"/>) land on the source's.</summary>
        public bool Anchor { get; set; } = true;

        /// <summary>World offset added to the anchor and the wrist goals, e.g. to stand the target beside the source.</summary>
        public Vector3 Offset { get; set; }

        /// <summary>True when both rigs have eye bones, so the anchor is eyes-on-eyes rather than head joint on head joint.</summary>
        public bool AnchorsEyes => _srcEyes != null;

        /// <summary>Number of target bones whose rest was re-aimed along SMPL's (6 for <see cref="LimbAims.Arms"/>, 2 for <see cref="LimbAims.Clavicles"/>).</summary>
        public int AimedJointCount { get; }

        public int JointCount => _solver.JointCount;
        public int TwistSegmentCount => _twists.Count;

        /// <summary>The source's anchor point: its eye midpoint, or its head joint.</summary>
        public Vector3 SourceAnchorPosition => _srcEyes?.EyesPosition ?? _src[kHead].position;

        /// <summary>The target's anchor point: its eye midpoint, or its head bone.</summary>
        public Vector3 TargetAnchorPosition => _tgtEyes?.EyesPosition ?? _tgt[kHead].position;

        /// <param name="source">Root of the posed SMPL avatar; its bones are found by name (<see cref="AnimUtils.BindBones"/>).</param>
        /// <param name="target">Humanoid Animator of the character that receives the pose, in its bind pose.</param>
        /// <param name="aims">Limbs to re-aim along SMPL's rest directions.</param>
        /// <exception cref="ArgumentException">The source lacks SMPL bones, or the target is not Humanoid.</exception>
        public SmplRetarget(Transform source, Animator target, LimbAims aims = LimbAims.Arms | LimbAims.Clavicles)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null || !target.isHuman) throw new ArgumentException("Target must be a Humanoid Animator.", nameof(target));

            _src = new Transform[AnimUtils.boneNum];
            AnimUtils.BindBones(source, ref _src);
            if (Array.IndexOf(_src, null) >= 0) throw new ArgumentException("Source is missing SMPL bones.", nameof(source));

            _tgt = new Transform[AnimUtils.boneNum];
            for (int i = 0; i < _tgt.Length; i++) _tgt[i] = target.GetBoneTransform(kHumanBone[i]);
            if (_tgt[kPelvis] == null || _tgt[kHead] == null) throw new ArgumentException("Target has no Hips or Head bone.", nameof(target));

            var restAims = RestAims(aims);
            var joints = new List<RetargetJoint>();
            for (int i = 0; i < AnimUtils.boneNum; i++)
            {
                if (_tgt[i] == null) continue; // optional Humanoid bone (UpperChest, Toes, ...) absent on this rig
                if (i == kLeftWrist) _leftHandJoint = joints.Count;
                if (i == kRightWrist) _rightHandJoint = joints.Count;
                if (restAims[i].IsSet) AimedJointCount++;
                joints.Add(new RetargetJoint(_src[i], _tgt[i], restAims[i]));
            }

            foreach (var (side, thumb, wrist) in new[] { ("left", HumanBodyBones.LeftThumbProximal, kLeftWrist), ("right", HumanBodyBones.RightThumbProximal, kRightWrist) })
            {
                var srcChains = new Transform[kSmplxFingers.Length][];
                var tgtChains = new Transform[kSmplxFingers.Length][];
                for (int f = 0; f < kSmplxFingers.Length; f++)
                {
                    var src = new Transform[3];
                    var tgt = new Transform[3];
                    for (int k = 0; k < 3; k++)
                    {
                        src[k] = AnimUtils.FindChildRecursive(source, $"{side}_{kSmplxFingers[f]}{k + 1}");
                        tgt[k] = target.GetBoneTransform(thumb + f * 3 + k);
                    }
                    if (Array.IndexOf(src, null) >= 0 || Array.IndexOf(tgt, null) >= 0) continue; // a rig without this finger keeps its own
                    srcChains[f] = src;
                    tgtChains[f] = tgt;

                    // Each phalanx aimed along SMPL-X's flat-hand phalanx: CC5's bind lays the fingers closer
                    // together and turns the thumb forward and down, so copying the bends alone bunched the
                    // fingers and tucked the thumb under the palm. SMPL-X has no fingertip joint, so the distal
                    // phalanx shares the intermediate's line (and swing), keeping the target's own last bend.
                    var proximal = new RestAim(src[0], src[1], tgt[0], tgt[1]);
                    var distal = new RestAim(src[1], src[2], tgt[1], tgt[2]);
                    var phalanxAims = new[] { proximal, distal, distal };
                    for (int k = 0; k < 3; k++)
                    {
                        _srcFingers.Add(src[k]);
                        joints.Add(new RetargetJoint(src[k], tgt[k], phalanxAims[k]));
                    }
                }

                if (Array.IndexOf(tgtChains, null, kIndex, kPinky - kIndex + 1) < 0) // spacing needs all four fingers
                {
                    // Knuckles are fixed in the hand, so the span is the same in every pose.
                    float scale = (tgtChains[kPinky][0].position - tgtChains[kIndex][0].position).magnitude
                                  / (srcChains[kPinky][0].position - srcChains[kIndex][0].position).magnitude;
                    _spreadHands.Add((_tgt[wrist], srcChains, tgtChains, scale));
                }
            }

            var srcLeftEye = AnimUtils.FindChildRecursive(source, "left_eye_smplhf");
            var srcRightEye = AnimUtils.FindChildRecursive(source, "right_eye_smplhf");
            var tgtLeftEye = target.GetBoneTransform(HumanBodyBones.LeftEye);
            var tgtRightEye = target.GetBoneTransform(HumanBodyBones.RightEye);
            if (srcLeftEye != null && tgtLeftEye != null) joints.Add(new RetargetJoint(srcLeftEye, tgtLeftEye));
            if (srcRightEye != null && tgtRightEye != null) joints.Add(new RetargetJoint(srcRightEye, tgtRightEye));

            _solver = new RetargetSolver(joints);
            CalibrateAgainstSourceRest(target.transform);

            // Read in head-local units, so valid whatever pose the heads are in from here on.
            if (srcLeftEye != null && srcRightEye != null && tgtLeftEye != null && tgtRightEye != null)
            {
                _srcEyes = new EyeAnchor(_src[kHead], srcLeftEye, srcRightEye);
                _tgtEyes = new EyeAnchor(_tgt[kHead], tgtLeftEye, tgtRightEye);
            }

            AddTwist(target, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm);
            AddTwist(target, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
            AddTwist(target, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm);
            AddTwist(target, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
            AddTwist(target, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg);
            AddTwist(target, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
            AddTwist(target, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg);
            AddTwist(target, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
        }

        // Per SMPL joint, the line its rest is aimed along (unset = keep the target's bind direction).
        RestAim[] RestAims(LimbAims aims)
        {
            var result = new RestAim[AnimUtils.boneNum];
            if ((aims & LimbAims.Arms) != 0)
            {
                foreach (int i in new[] { kLeftShoulder, kLeftElbow, kLeftWrist })
                    result[i] = new RestAim(_src[kLeftShoulder], _src[kLeftWrist], _tgt[kLeftShoulder], _tgt[kLeftWrist]);
                foreach (int i in new[] { kRightShoulder, kRightElbow, kRightWrist })
                    result[i] = new RestAim(_src[kRightShoulder], _src[kRightWrist], _tgt[kRightShoulder], _tgt[kRightWrist]);
            }
            if ((aims & LimbAims.Clavicles) != 0)
            {
                result[kLeftCollar] = new RestAim(_src[kLeftCollar], _src[kLeftShoulder], _tgt[kLeftCollar], _tgt[kLeftShoulder], headingOnly: true);
                result[kRightCollar] = new RestAim(_src[kRightCollar], _src[kRightShoulder], _tgt[kRightCollar], _tgt[kRightShoulder], headingOnly: true);
            }
            return result;
        }

        // Poses the SMPL source into the rest its rotations are relative to (all joints identity;
        // AnimUtils.SetPose adds the pelvis up-axis fix), calibrates, then restores the source.
        // The fingers' rest is the flat hand, also identity: the SMPL-X FBX comes with them curled
        // (the relaxed mean hand), which would otherwise be taken as the target's straight bind.
        void CalibrateAgainstSourceRest(Transform targetRoot)
        {
            var saved = new Quaternion[AnimUtils.boneNum];
            for (int i = 0; i < saved.Length; i++) saved[i] = _src[i].localRotation;
            var savedFingers = new Quaternion[_srcFingers.Count];
            for (int i = 0; i < savedFingers.Length; i++) savedFingers[i] = _srcFingers[i].localRotation;
            var rest = new Quaternion[AnimUtils.boneNum];
            for (int i = 0; i < rest.Length; i++) rest[i] = Quaternion.identity;
            var src = _src;
            AnimUtils.SetPose(ref src, ref rest);
            foreach (var finger in _srcFingers) finger.localRotation = Quaternion.identity;

            // Canonical up: SMPL's template is Y-up in its own joint frames, which at rest all equal the
            // pelvis's; the target's bind stands upright on its root.
            var sourceFacing = SkeletonFacing.FromJoints(_src[kPelvis].rotation * Vector3.up,
                _src[kLeftHip].position, _src[kRightHip].position);
            var targetFacing = SkeletonFacing.FromJoints(targetRoot.up,
                _tgt[kLeftHip].position, _tgt[kRightHip].position);
            _solver.Calibrate(sourceFacing, targetFacing);

            for (int i = 0; i < saved.Length; i++) _src[i].localRotation = saved[i];
            for (int i = 0; i < savedFingers.Length; i++) _srcFingers[i].localRotation = savedFingers[i];
        }

        // Turns the index, ring and pinky sideways about the palm normal, the middle finger fixed, until the gaps
        // between neighbouring last joints are the source's current ones scaled by the knuckle span. The phalanx
        // aims copy SMPL-X's spread angles, but CC5's knuckles sit closer together (Aaron's middle–ring 20 mm,
        // SMPL-X's 28), so the same angles crossed the middle and ring fingers once the relaxed hand turned them
        // towards each other (13 mm apart where SMPL-X's are 25). Matched per frame, not once at rest: matching
        // the flat hand's gaps still left them 16 mm apart once curled.
        static void SpreadFingers(Transform hand, Transform[][] src, Transform[][] tgt, float scale)
        {
            var normal = Vector3.Cross(tgt[kPinky][0].position - tgt[kIndex][0].position, tgt[kMiddle][0].position - hand.position).normalized;
            float Gap(int f) => scale * (src[f + 1][2].position - src[f][2].position).magnitude;
            Turn(tgt[kIndex], normal, tgt[kMiddle][2].position, Gap(kIndex));
            Turn(tgt[kRing], normal, tgt[kMiddle][2].position, Gap(kMiddle));
            Turn(tgt[kPinky], normal, tgt[kRing][2].position, Gap(kRing)); // after the ring has moved
        }

        // Turns a finger at its knuckle about `axis` so its last joint lands `gap` from `neighbour`: Newton steps
        // from no turn, within ±kMaxSpreadDegrees. Turning the proximal phalanx carries the whole finger, the
        // others keeping the local rotations the solver gave them.
        static void Turn(Transform[] finger, Vector3 axis, Vector3 neighbour, float gap)
        {
            var knuckle = finger[0].position;
            var last = finger[2].position - knuckle;
            float Error(float degrees) => (knuckle + Quaternion.AngleAxis(degrees, axis) * last - neighbour).magnitude - gap;
            float turn = 0f;
            for (int i = 0; i < 8; i++)
            {
                float slope = Error(turn + 0.5f) - Error(turn - 0.5f); // per degree
                if (Mathf.Abs(slope) < 1e-6f) break; // finger curled along the axis: turning it can't move the joint
                turn = Mathf.Clamp(turn - Error(turn) / slope, -kMaxSpreadDegrees, kMaxSpreadDegrees);
            }
            finger[0].rotation = Quaternion.AngleAxis(turn, axis) * finger[0].rotation;
        }

        void AddTwist(Animator target, HumanBodyBones segmentBone, HumanBodyBones distalBone)
        {
            var segment = target.GetBoneTransform(segmentBone);
            var distal = target.GetBoneTransform(distalBone);
            if (segment == null || distal == null || distal.parent != segment) return;

            var bones = new List<Transform>();
            CollectTwistBones(segment, distal, bones);
            if (bones.Count > 0) _twists.Add(new TwistDistributor(segment, distal, bones));
        }

        // Depth-first, parents before children, skipping the distal joint's own subtree.
        static void CollectTwistBones(Transform parent, Transform distal, List<Transform> into)
        {
            foreach (Transform child in parent)
            {
                if (child == distal) continue;
                if (child.name.IndexOf("twist", StringComparison.OrdinalIgnoreCase) >= 0) into.Add(child);
                CollectTwistBones(child, distal, into);
            }
        }

        /// <summary>Copies the source's current pose onto the target.</summary>
        public void Apply()
        {
            _solver.Apply();
            foreach (var (hand, src, tgt, scale) in _spreadHands) SpreadFingers(hand, src, tgt, scale);

            if (PinWrists)
            {
                var shift = Anchor ? SourceAnchorPosition + Offset - TargetAnchorPosition : Vector3.zero;
                PinWrist(kLeftShoulder, kLeftElbow, kLeftWrist, _leftHandJoint, shift);
                PinWrist(kRightShoulder, kRightElbow, kRightWrist, _rightHandJoint, shift);
            }

            if (DistributeTwist)
            {
                foreach (var t in _twists) t.Apply();
            }

            if (Anchor)
                _tgt[kPelvis].position += SourceAnchorPosition + Offset - TargetAnchorPosition;
        }

        // Solved before the anchor moves the body, so the goal is taken back by that shift.
        void PinWrist(int shoulder, int elbow, int wrist, int handJoint, Vector3 shift)
        {
            if (_tgt[shoulder] == null || _tgt[elbow] == null || _tgt[wrist] == null || handJoint < 0) return;
            AnimUtils.SolveTwoBoneIK(_tgt[shoulder], _tgt[elbow], _tgt[wrist], _src[wrist].position + Offset - shift);
            _solver.Apply(handJoint); // IK turned the hand with the arm; restore its orientation
        }
    }
}
