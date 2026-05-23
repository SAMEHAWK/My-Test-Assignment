using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// Ragdoll 系统运行参数（双骨架/单骨架兜底共用）
    /// Runtime parameters for ragdoll system (shared by dual-skeleton and legacy fallback)
    /// </summary>
    [CreateAssetMenu(fileName = "RagdollSystemConfig", menuName = "Active Ragdoll/Ragdoll System Config")]
    public sealed class RagdollSystemConfig : ScriptableObject
    {
        [Header("Settle / 沉降")]
        [Tooltip("击倒最小时长（秒）— Minimum knockdown duration in seconds")]
        public float minKnockdownDuration = 0.8f;
        [Tooltip("无刚体占位沉降时长（秒）— Placeholder settle duration when no ragdoll bodies")]
        public float placeholderSettleDuration = 1.5f;
        [Tooltip("沉降速度阈值（m/s）— Velocity threshold for settle check")]
        public float settleSpeedThreshold = 0.15f;
        [Tooltip("速度低于阈值后需持续稳定的时长（秒）— Required stable duration below settle threshold")]
        public float settleStableDuration = 0.15f;

        [Header("Heavy Partial / 重击局部")]
        [Tooltip("局部受击选骨半径（米）— Selection radius for partial reaction")]
        public float heavyPartialRadius = 0.65f;
        [Tooltip("局部受击最多选中骨骼数 — Max selected bones for partial reaction")]
        public int heavyPartialMaxBodies = 6;
        [Tooltip("局部受击保持动态物理时长（秒）— Dynamic physics hold duration")]
        public float heavyPartialPhysicsHoldDuration = 0.18f;
        [Tooltip("主受击骨冲量倍率 — Impulse multiplier for primary hit bone")]
        public float heavyPartialPrimaryImpulseMultiplier = 0.85f;
        [Tooltip("次级骨冲量倍率 — Impulse multiplier for secondary bones")]
        public float heavyPartialSecondaryImpulseMultiplier = 0.45f;
        [Tooltip("角冲量倍率 — Angular impulse multiplier")]
        public float heavyPartialAngularImpulseMultiplier = 0.18f;
        [Tooltip("局部受击临时 Solver Iterations — Temporary solver iterations")]
        public int heavyPartialSolverIterations = 12;
        [Tooltip("局部受击临时 Solver Velocity Iterations — Temporary solver velocity iterations")]
        public int heavyPartialSolverVelocityIterations = 4;
        [Tooltip("局部回融时长（秒）— Blend-back duration")]
        public float heavyPartialBlendBackDuration = 0.75f;
        [Tooltip("局部回融曲线 — Blend-back curve")]
        public AnimationCurve heavyPartialBlendBackCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    }
}
