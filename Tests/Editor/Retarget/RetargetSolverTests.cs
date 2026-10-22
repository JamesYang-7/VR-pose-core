using NUnit.Framework;
using UnityEngine;
using VrChat.Retarget;

namespace VrChat.Testing.Retarget
{
    /// <summary>
    /// Source: a SMPL-like two-bone chain with identity rest frames and its joints along +X.
    /// Target: a CC5-like chain whose bones run along their local +Y, with an arbitrary roll, a bend
    /// and different bone lengths — its bone axes and joint placement match nothing on the source.
    /// </summary>
    [TestFixture]
    public class RetargetSolverTests
    {
        GameObject _root;
        Transform _s0, _s1, _s2;
        Transform _t0, _t1, _t2;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("RetargetSolverTests");
            _s0 = NewBone("s0", _root.transform, Vector3.zero, Quaternion.identity);
            _s1 = NewBone("s1", _s0, Vector3.right, Quaternion.identity);
            _s2 = NewBone("s2", _s1, Vector3.right, Quaternion.identity);

            _t0 = NewBone("t0", _root.transform, Vector3.zero, Quaternion.Euler(0f, 37f, -80f));
            _t1 = NewBone("t1", _t0, new Vector3(0f, 0.6f, 0f), Quaternion.Euler(10f, 0f, 25f));
            _t2 = NewBone("t2", _t1, new Vector3(0f, 1.3f, 0f), Quaternion.identity);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        static Transform NewBone(string name, Transform parent, Vector3 localPosition, Quaternion localRotation)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            t.localRotation = localRotation;
            return t;
        }

        RetargetSolver NewCalibratedSolver(Quaternion targetFacing)
        {
            var solver = new RetargetSolver(new[] { new RetargetJoint(_s0, _t0), new RetargetJoint(_s1, _t1) });
            solver.Calibrate(Quaternion.identity, targetFacing);
            return solver;
        }

        [Test]
        public void Apply_SourceAtRest_TargetStaysInItsBindPose()
        {
            var bind0 = _t0.localRotation;
            var bind1 = _t1.localRotation;
            var sut = NewCalibratedSolver(Quaternion.identity);

            sut.Apply();

            Assert.That(Quaternion.Angle(_t0.localRotation, bind0), Is.LessThan(1e-2f), "t0");
            Assert.That(Quaternion.Angle(_t1.localRotation, bind1), Is.LessThan(1e-2f), "t1");
        }

        [Test]
        public void Apply_SourceJointBent_TargetJointBendsByTheSameRotation()
        {
            var bind1 = _t1.rotation;
            var sut = NewCalibratedSolver(Quaternion.identity);
            _s1.localRotation = Quaternion.Euler(20f, -35f, 50f);

            sut.Apply();

            var sourceDelta = _s1.rotation;
            var targetDelta = _t1.rotation * Quaternion.Inverse(bind1);
            Assert.That(Quaternion.Angle(targetDelta, sourceDelta), Is.LessThan(1e-2f));
        }

        [Test]
        public void Apply_SourceParentRotated_TargetChildKeepsItsBindLocalRotation()
        {
            var bind1 = _t1.localRotation;
            var sut = NewCalibratedSolver(Quaternion.identity);
            _s0.localRotation = Quaternion.Euler(0f, 30f, 60f);

            sut.Apply();

            Assert.That(Quaternion.Angle(_t1.localRotation, bind1), Is.LessThan(1e-2f));
        }

        // Both target joints aimed along one line, s0→s2 on the source and t0→t2 on the target, as
        // SmplRetarget aims an arm's upper arm and forearm along shoulder→wrist.
        RetargetSolver NewSolverAimedAlongTheChain(bool headingOnly)
        {
            var aim = new RestAim(_s0, _s2, _t0, _t2, headingOnly);
            var solver = new RetargetSolver(new[] { new RetargetJoint(_s0, _t0, aim), new RetargetJoint(_s1, _t1, aim) });
            solver.Calibrate(Quaternion.identity, Quaternion.identity);
            return solver;
        }

        Vector3 TargetLine => _t2.position - _t0.position;

        [Test]
        public void Apply_AimedAndSourceAtRest_TargetLinePointsAlongTheSourceLine()
        {
            var sut = NewSolverAimedAlongTheChain(headingOnly: false);

            sut.Apply();

            Assert.That(Vector3.Angle(TargetLine, _s2.position - _s0.position), Is.LessThan(1e-2f));
        }

        [Test]
        public void Apply_AimedAndSourceChainTurned_TargetLineStaysAlongTheSourceLine()
        {
            var sut = NewSolverAimedAlongTheChain(headingOnly: false);
            _s0.localRotation = Quaternion.Euler(-30f, 50f, 20f);

            sut.Apply();

            Assert.That(Vector3.Angle(TargetLine, _s2.position - _s0.position), Is.LessThan(1e-2f));
        }

        [Test]
        public void Apply_AimSharedByBothJoints_TargetKeepsItsBindAngleBetweenTheBones()
        {
            var bind1 = _t1.localRotation;
            var sut = NewSolverAimedAlongTheChain(headingOnly: false);

            sut.Apply();

            Assert.That(Quaternion.Angle(_t1.localRotation, bind1), Is.LessThan(1e-2f));
        }

        [Test]
        public void Apply_HeadingOnlyAim_TargetLineTakesTheSourceHeading()
        {
            var sut = NewSolverAimedAlongTheChain(headingOnly: true);

            sut.Apply();

            Assert.That(Vector3.Angle(Vector3.ProjectOnPlane(TargetLine, Vector3.up), _s2.position - _s0.position), Is.LessThan(1e-2f));
        }

        [Test]
        public void Apply_HeadingOnlyAim_TargetLineKeepsItsBindElevation()
        {
            float bindElevation = Vector3.Angle(TargetLine, Vector3.up);
            var sut = NewSolverAimedAlongTheChain(headingOnly: true);

            sut.Apply();

            Assert.That(Vector3.Angle(TargetLine, Vector3.up), Is.EqualTo(bindElevation).Within(1e-2f));
        }

        [Test]
        public void Apply_TargetFacingAway_TargetBindIsTurnedToTheSourceFacing()
        {
            var bind0 = _t0.rotation;
            var sut = NewCalibratedSolver(Quaternion.Euler(0f, 180f, 0f));

            sut.Apply();

            Assert.That(Quaternion.Angle(_t0.rotation, Quaternion.Euler(0f, 180f, 0f) * bind0), Is.LessThan(1e-2f));
        }
    }
}
