using UnityEngine;

namespace VrChat.Ik
{
    /// <summary>
    /// A 22-joint SMPL-X body posed by IK alone from three targets: the eyes and the head's rotation (from the headset)
    /// and each wrist's pose (from its controller). The legs never bend: they keep the rest pose, standing. The upper
    /// body is what moves, the same joints whether the body stands or a seat re-poses its legs afterwards.
    ///
    /// <para>Each <see cref="Solve"/> starts from the rest pose (every joint rig-local identity: the SMPL-X T-pose,
    /// standing upright and facing the rest forward) and goes down the chains:</para>
    /// <list type="number">
    /// <item><b>Heading.</b> The body turns with the head only past <see cref="TurnDeadZone"/>. Within it the spine
    /// takes the turn, a share at each joint, most at the neck; the head turns the rest of the way.</item>
    /// <item><b>Pelvis.</b> It stays at the height it had under the eyes on the first solve (the wearer stands or sits
    /// upright then, as calibration asks). It follows the eyes across the floor only past <see cref="FollowRadius"/>:
    /// within it, the spine bends to reach them. The body slides; it takes no steps.</item>
    /// <item><b>Spine and head.</b> The head takes its target rotation, and <see cref="SpineLean.Reach"/> leans the
    /// pelvis over the hips and curls the spine so the eyes reach their target, the thighs still. What the spine
    /// can't reach (a crouch), the whole body moves by, so the eyes always land on the headset, as a pose model's
    /// body does.</item>
    /// <item><b>Arms.</b> A collar shrugs towards a wrist target beyond <see cref="ShrugStart"/> of the arm's reach,
    /// by at most <see cref="MaxShrug"/> degrees and never down. Then <see cref="TwoBoneIk.Solve"/> bends the elbow
    /// down, back and out from the chest (<see cref="ElbowHint"/>), and the wrist takes its target rotation.</item>
    /// </list>
    /// <para>No joint limits and no smoothing: what the targets say, every frame.</para>
    /// </summary>
    public sealed class IkBody
    {
        /// <summary>How far each arm reaches, as a share of its length, before its collar starts to shrug.</summary>
        public const float ShrugStart = 0.75f;

        /// <summary>The most a collar shrugs (degrees), at full reach.</summary>
        public const float MaxShrug = 20f;

        /// <summary>The side an elbow bends to, in the chest's frame: (out from the body, up, forward).</summary>
        public static readonly Vector3 ElbowHint = new(0.4f, -1f, -0.5f);

        const int kPelvis = 0, kLeftHip = 1, kRightHip = 2, kSpine3 = 9, kHead = 15;
        // spine1, spine2, spine3 and the neck: the chain SpineLean bends.
        static readonly int[] BackJoints = { 3, 6, 9, 12 };
        // The share of the head's turn from the body each of spine1, spine2, spine3 and the neck adds; the head takes
        // the rest (here 30%) by taking its target rotation.
        static readonly float[] TwistShares = { 0.1f, 0.15f, 0.2f, 0.25f };

        sealed class Arm
        {
            public Transform Collar, Shoulder, Elbow, Wrist;
            public Quaternion RestShoulder; // world, at rest
            public Vector3 Out; // shoulder to elbow at rest, level: out from the body
            public float Length; // shoulder to elbow to wrist
            public Vector3 Bend; // the last solve's bend side
        }

        readonly Transform[] _bones;
        readonly Transform[] _back;
        readonly EyeAnchor _eyes;
        readonly Vector3 _forward;
        readonly Quaternion _restHead;
        readonly Quaternion _restChest;
        readonly Vector3 _eyesFromPelvis; // at rest, world
        readonly Arm _left;
        readonly Arm _right;
        float _headYaw;
        float _bodyYaw;
        Vector3 _stance;

        /// <summary>How far (m) the eyes may move across the floor from over the pelvis before the body follows.</summary>
        public float FollowRadius { get; set; } = 0.15f;

        /// <summary>How far (degrees) the head may turn from the body before the body turns with it.</summary>
        public float TurnDeadZone { get; set; } = 40f;

        /// <summary>True once <see cref="Solve"/> has run and set the pelvis height.</summary>
        public bool IsStarted { get; private set; }

        /// <summary>World height of the pelvis, taken under the eyes on the first <see cref="Solve"/>.</summary>
        public float PelvisHeight { get; private set; }

        /// <summary>Where the pelvis stands across the floor (world; its height is <see cref="PelvisHeight"/>).</summary>
        public Vector3 Stance => new(_stance.x, PelvisHeight, _stance.z);

        /// <summary>The body's heading (degrees about the vertical, from the rest forward).</summary>
        public float Heading => _bodyYaw;

        /// <param name="bones">The 22 SMPL joints in <c>AnimUtils.boneNames</c> order; the pelvis's parent is the rig's root bone, which <see cref="Solve"/> moves.</param>
        /// <param name="eyes">Where the eyes sit on the head bone.</param>
        /// <param name="forward">The way the rest pose faces (the rig root's forward), level.</param>
        public IkBody(Transform[] bones, EyeAnchor eyes, Vector3 forward)
        {
            _bones = bones;
            _eyes = eyes;
            _forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            _back = new Transform[BackJoints.Length];
            for (var i = 0; i < BackJoints.Length; i++) _back[i] = bones[BackJoints[i]];

            var saved = new Quaternion[bones.Length];
            for (var j = 0; j < bones.Length; j++) saved[j] = bones[j].localRotation;
            Rest();
            _restHead = bones[kHead].rotation;
            _restChest = bones[kSpine3].rotation;
            _eyesFromPelvis = eyes.EyesPosition - bones[kPelvis].position;
            _left = RestArm(13, 16, 18, 20);
            _right = RestArm(14, 17, 19, 21);
            for (var j = 0; j < bones.Length; j++) bones[j].localRotation = saved[j];
        }

        /// <summary>Takes the pelvis height and place again on the next <see cref="Solve"/>.</summary>
        public void Restart() => IsStarted = false;

        /// <summary>Poses the body so its eyes land on <paramref name="eyes"/> and its wrists reach their targets.</summary>
        /// <param name="eyes">Where the eyes go (the headset), world.</param>
        /// <param name="head">The head bone's world rotation.</param>
        /// <param name="leftWrist">The left wrist joint's world pose.</param>
        /// <param name="rightWrist">The right wrist joint's world pose.</param>
        public void Solve(Vector3 eyes, Quaternion head, Pose leftWrist, Pose rightWrist)
        {
            Rest();
            var pelvis = _bones[kPelvis];
            var root = pelvis.parent;

            // Heading: looking straight up or down keeps the last one.
            var headForward = Vector3.ProjectOnPlane(head * Quaternion.Inverse(_restHead) * _forward, Vector3.up);
            if (headForward.sqrMagnitude > 1e-4f) _headYaw = Vector3.SignedAngle(_forward, headForward, Vector3.up);
            if (!IsStarted) _bodyYaw = _headYaw;
            var turn = Mathf.DeltaAngle(_bodyYaw, _headYaw);
            if (Mathf.Abs(turn) > TurnDeadZone)
            {
                var follow = turn - Mathf.Sign(turn) * TurnDeadZone;
                _bodyYaw += follow;
                turn -= follow;
            }
            var facing = Quaternion.AngleAxis(_bodyYaw, Vector3.up);

            // The pelvis, upright under the eyes or as near as FollowRadius lets it stay where it was.
            var under = eyes - facing * _eyesFromPelvis;
            if (!IsStarted)
            {
                PelvisHeight = under.y;
                _stance = under;
                IsStarted = true;
            }
            var drift = Vector3.ProjectOnPlane(under - _stance, Vector3.up);
            if (drift.magnitude > FollowRadius) _stance += drift * (1f - FollowRadius / drift.magnitude);
            pelvis.rotation = facing * pelvis.rotation;
            root.position += Stance - pelvis.position;

            for (var i = 0; i < _back.Length; i++)
                _back[i].rotation = Quaternion.AngleAxis(turn * TwistShares[i], Vector3.up) * _back[i].rotation;

            // With the head's rotation set, its eyes sit at a fixed offset from it.
            var headBone = _bones[kHead];
            headBone.rotation = head;
            var eyesFromHead = _eyes.EyesPosition - headBone.position;
            SpineLean.Reach(root, pelvis, _bones[kLeftHip], _bones[kRightHip], _back, headBone, eyes - eyesFromHead);
            headBone.rotation = head;
            root.position += eyes - _eyes.EyesPosition;

            var chest = _bones[kSpine3].rotation * Quaternion.Inverse(_restChest);
            SolveArm(_left, leftWrist, chest);
            SolveArm(_right, rightWrist, chest);
        }

        void SolveArm(Arm arm, Pose wrist, Quaternion chest)
        {
            var up = chest * Vector3.up;
            var reach = (wrist.position - arm.Shoulder.position).magnitude / arm.Length;
            var shrug = Mathf.Clamp01((reach - ShrugStart) / (1f - ShrugStart));
            if (shrug > 0f)
            {
                var toWrist = wrist.position - arm.Collar.position;
                var height = Vector3.Dot(toWrist, up);
                if (height < 0f) toWrist -= height * up; // a collar never shrugs down
                if (toWrist.sqrMagnitude > 1e-6f)
                {
                    var swing = Quaternion.FromToRotation(arm.Shoulder.position - arm.Collar.position, toWrist);
                    arm.Collar.rotation = Quaternion.RotateTowards(Quaternion.identity, swing, MaxShrug * shrug) * arm.Collar.rotation;
                }
            }

            var hint = chest * (ElbowHint.x * arm.Out + ElbowHint.y * Vector3.up + ElbowHint.z * _forward);
            // From a T-pose the forearm flexes forward; the shoulder carries that side along as it turns.
            var flex = arm.Shoulder.rotation * Quaternion.Inverse(arm.RestShoulder) * _forward;
            TwoBoneIk.Solve(arm.Shoulder, arm.Elbow, arm.Wrist, wrist.position, hint, flex, ref arm.Bend);
            arm.Wrist.rotation = wrist.rotation;
        }

        // The rest pose: every joint rig-local identity, which for the SMPL-X rig is its T-pose standing (a SMPL root
        // standing upright in the Z-up world, through GorpPose's Z-up fix, is identity too).
        void Rest()
        {
            foreach (var bone in _bones) bone.localRotation = Quaternion.identity;
        }

        Arm RestArm(int collar, int shoulder, int elbow, int wrist)
        {
            var arm = new Arm
            {
                Collar = _bones[collar], Shoulder = _bones[shoulder], Elbow = _bones[elbow], Wrist = _bones[wrist],
            };
            arm.RestShoulder = arm.Shoulder.rotation;
            arm.Out = Vector3.ProjectOnPlane(arm.Elbow.position - arm.Shoulder.position, Vector3.up).normalized;
            arm.Length = (arm.Elbow.position - arm.Shoulder.position).magnitude
                         + (arm.Wrist.position - arm.Elbow.position).magnitude;
            return arm;
        }
    }
}
