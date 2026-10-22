using UnityEngine;

namespace VrChat.Retarget
{
    /// <summary>One source→target joint pair for <see cref="RetargetSolver"/>.</summary>
    public readonly struct RetargetJoint
    {
        public Transform Source { get; }
        public Transform Target { get; }

        /// <summary>Optional rest re-aim; unset keeps the target's own bind direction.</summary>
        public RestAim Aim { get; }

        public RetargetJoint(Transform source, Transform target, RestAim aim = default)
        {
            Source = source;
            Target = target;
            Aim = aim;
        }
    }
}
