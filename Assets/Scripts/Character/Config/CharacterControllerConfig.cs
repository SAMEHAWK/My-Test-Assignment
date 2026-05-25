using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 角色控制器可调参数（移动、平衡、受击时长；不含 Animator）
    /// Tunable locomotion, balance, hit timings — no animator settings
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterControllerConfig", menuName = "Active Ragdoll/Character Controller Config")]
    public sealed class CharacterControllerConfig : ScriptableObject
    {
        [Header("Locomotion / 移动")]
        [Tooltip("平面最大移动速度 m/s（未拔刀）— Max planar move speed while weapon stowed")]
        public float moveSpeed = 5f;

        [Tooltip("拔刀后最大移动速度 m/s；≤0 则与 moveSpeed 相同 — Max speed while weapon equipped; ≤0 uses moveSpeed")]
        public float equippedMoveSpeed = 4f;

        [Tooltip("加速率 m/s² — Acceleration toward max speed")]
        public float moveAcceleration = 20f;

        [Tooltip("减速率 m/s² — Deceleration when input released")]
        public float moveDeceleration = 20f;

        [Tooltip("重击时移动速度倍率 — Move speed multiplier during heavy stagger")]
        [Range(0f, 1f)]
        public float heavyMoveSpeedMultiplier = 0.5f;

        [Tooltip("朝向移动方向插值速度 — Rotation smoothing toward move direction")]
        public float rotationSpeed = 12f;

        [Header("Grounding / 贴地")]
        [Tooltip("CharacterController 垂直重力加速度 — Vertical gravity for CharacterController grounding")]
        public float gravity = -25f;

        [Tooltip("接地时保持的小下压力速度 — Small downward velocity while grounded")]
        public float groundedStickVelocity = -2f;

        [Tooltip("最大下落速度（正数，内部取负）— Max fall speed as a positive value")]
        public float maxFallSpeed = 30f;

        [Header("Balance / 平衡")]
        [Tooltip("平衡值上限（归零触发击倒）— Max balance before knockdown threshold")]
        public int maxBalance = 6;
        [Tooltip("轻击扣除平衡值 — Balance damage from light hit")]
        public int lightBalanceDamage = 1;
        [Tooltip("重击扣除平衡值 — Balance damage from heavy hit")]
        public int heavyBalanceDamage = 2;
        [Tooltip("距离上次受击多少秒后开始恢复 — Delay after last hit before balance regeneration starts")]
        public float balanceRegenDelay = 2f;
        [Tooltip("每秒恢复多少平衡值（按帧累加）— Balance regenerated per second")]
        public float balanceRegenPerSecond = 2f;

        [Header("Hit Timings / 受击时长")]
        [Tooltip("轻击表现总时长秒数（overlay 淡出）— Light hit presentation duration")]
        public float lightFlinchDuration = 0.3f;
        [Tooltip("重击回到可控状态前的混合时长 — Heavy stagger blend-back duration")]
        public float heavyBlendBackDuration = 0.75f;

        [Header("Heavy Root Motion / 重击根运动")]
        [Tooltip("重击期间是否提取动画根运动并驱动 Player 根节点 — Consume heavy root motion and drive player root")]
        public bool heavyStaggerUseRootMotion = true;

        [Tooltip("重击根运动位移倍率（仅 XZ）— Root-motion planar distance multiplier during heavy stagger")]
        public float heavyRootMotionPlanarScale = 1f;

        [Tooltip("每帧最大根运动位移（米，0 表示不限制）— Max planar root-motion delta per frame in meters (0 = unlimited)")]
        public float heavyRootMotionMaxDeltaPerFrame = 0.45f;

        [Header("Attack Root Motion / 攻击根运动")]
        [Tooltip("攻击期间是否提取动画根运动并驱动 Player 根节点 — Consume attack root motion and drive player root")]
        public bool attackUseRootMotion = true;

        [Tooltip("攻击根运动位移倍率（仅 XZ）— Root-motion planar distance multiplier during attacks")]
        public float attackRootMotionPlanarScale = 1f;

        [Tooltip("攻击每帧最大根运动位移（米，0 表示不限制）— Max attack planar root-motion delta per frame in meters (0 = unlimited)")]
        public float attackRootMotionMaxDeltaPerFrame = 0.45f;

        [Header("Light Hit / 轻击（动画 + 脊柱弯曲）")]
        [Tooltip("根节点 CC 后撤 m；0 = 脚不离地 — Optional root knockback; 0 = no slide")]
        public float lightFlinchRootKnockbackDistance;

        [Tooltip("程序化脊柱反向弯曲 — Procedural spine recoil")]
        public bool lightFlinchSpineRecoilEnabled = true;

        [Tooltip("脊柱前后倾角度（度）— Spine pitch degrees (front/back hit)")]
        public float lightFlinchSpinePitchDegrees = 8f;

        [Tooltip("脊柱左右倾角度（度）— Spine roll degrees (left/right hit)")]
        public float lightFlinchSpineRollDegrees = 6f;

        [Tooltip("程序化颈部反向弯曲 — Procedural neck recoil (optional)")]
        public bool lightFlinchNeckRecoilEnabled;

        [Tooltip("颈部前后倾（度）— Neck pitch degrees")]
        public float lightFlinchNeckPitchDegrees = 4f;

        [Tooltip("颈部左右倾（度）— Neck roll degrees")]
        public float lightFlinchNeckRollDegrees = 3f;

        [Tooltip("程序化头部反向弯曲 — Procedural head recoil")]
        public bool lightFlinchHeadRecoilEnabled = true;

        [Tooltip("头部前后倾（度）— Head pitch degrees")]
        public float lightFlinchHeadPitchDegrees = 5f;

        [Tooltip("头部左右倾（度）— Head roll degrees")]
        public float lightFlinchHeadRollDegrees = 4f;

        [Tooltip("弯曲强度曲线（横轴 0~1 为 overlay 进度）；脊柱/颈/头共用 — Recoil strength curve")]
        public AnimationCurve lightFlinchSpineRecoilCurve;

        /// <summary>
        /// 当前是否拔刀状态下的移动速度上限（用于 Locomotion 与 Speed 归一化）
        /// Max move speed for stowed vs equipped weapon
        /// </summary>
        public float GetMaxMoveSpeed(bool weaponEquipped)
        {
            if (!weaponEquipped || equippedMoveSpeed <= 0.01f)
                return moveSpeed;

            return equippedMoveSpeed;
        }
    }
}
