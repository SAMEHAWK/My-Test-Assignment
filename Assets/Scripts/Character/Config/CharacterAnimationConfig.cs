using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 角色动画与装备 overlay 参数（ScriptableObject）
    /// Animator parameters, layers, and weapon equip overlay tuning
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterAnimationConfig", menuName = "Active Ragdoll/Character Animation Config")]
    public sealed class CharacterAnimationConfig : ScriptableObject
    {
        [Header("Animator Parameters / 动画参数名")]
        public string speedParam = "Speed";
        public string hitBlendXParam = "HitBlendX";
        public string hitBlendZParam = "HitBlendZ";
        public string flinchWeightParam = "FlinchWeight";

        [Header("Light Flinch Layer / 轻击层")]
        [Tooltip("Animator 中 Flinch 层名称 — Flinch layer name in controller")]
        public string flinchLayerName = "FlinchLayer";

        [Header("Heavy Stagger / 重击表现")]
        [Tooltip("重击专用状态名（建议放 FullBodyLayer）— Dedicated heavy-stagger state name (recommended on FullBodyLayer)")]
        public string heavyStaggerStateName = "HitHeavy";

        [Tooltip("重击状态 CrossFade 时长（秒）— CrossFade duration for heavy-stagger state")]
        public float heavyStaggerCrossFadeDuration = 0.08f;

        [Header("Weapon Equip / 装备武器")]
        public string movingParam = "Moving";
        public string equippedParam = "Equipped";
        public string upBodyLayerName = "UpBodyLayer";
        public string fullBodyLayerName = "FullBodyLayer";
        [Tooltip("装备武器时播放重击的无手臂层名称 — No-arm heavy-hit layer name used while weapon is equipped")]
        public string noArmLayerName = "NoArmLayer";

        [Header("Attack / 主动攻击")]
        [Tooltip("主动攻击层名称 — Player attack layer name")]
        public string attackLayerName = "AttackLayer";
        [Tooltip("主动攻击轻攻击状态名 — Light attack state name")]
        public string attackLightStateName = "AttackLight";
        [Tooltip("主动攻击重攻击状态名 — Heavy attack state name")]
        public string attackHeavyStateName = "AttackHeavy";
        [Tooltip("攻击状态 CrossFade 时长（秒）— CrossFade duration for attack states")]
        public float attackCrossFadeDuration = 0.06f;
        [Tooltip("攻击层淡出时长（秒）— Attack layer fade-out duration")]
        public float attackOverlayFadeOutDuration = 0.08f;
        [Tooltip("攻击动画事件缺失时的兜底完成时长（秒）— Fallback completion duration when attack event is missing")]
        public float attackFallbackDuration = 1.2f;

        [Tooltip("归一化 Speed 超过此值视为移动 — Normalized speed above = moving")]
        [Range(0f, 1f)]
        public float movingSpeedThreshold = 0.1f;

        [Header("Weapon Equip States / 装备状态名（与 Animator 各 Layer 状态名一致）")]
        [Tooltip("FullBodyLayer 上全身装备状态 — Full-body equip state on FullBodyLayer")]
        public string equipFullBodyStateName = "wp_Equip";

        [Tooltip("UpBodyLayer 上上半身装备 — Upper-body equip on UpBodyLayer")]
        public string equipUpBodyStateName = "wp_Equip_inplace";

        [Tooltip("FullBodyLayer 上全身收回 — Full-body unequip")]
        public string unequipFullBodyStateName = "wp_Unequip";

        [Tooltip("UpBodyLayer 上上半身收回 — Upper-body unequip")]
        public string unequipUpBodyStateName = "wp_Unequip_inplace";

        [Header("Weapon Equip Playback / 装备播片")]
        [Tooltip("CrossFade 时长（秒）— CrossFade duration in seconds")]
        public float equipCrossFadeDuration = 0.1f;

        [Tooltip("播片中途 FullBody↔UpBody 切层 CrossFade 时长 — Mid-playback layer switch crossfade")]
        public float equipMovingLayerSwitchCrossFade = 0.1f;

        [Tooltip("持续移动多少秒后才切到 UpBody — Seconds moving before switch to UpBody")]
        public float equipLayerSwitchMoveHoldSeconds = 0.2f;

        [Tooltip("持续停下多少秒后才切回 FullBody — Seconds idle before switch to FullBody")]
        public float equipLayerSwitchIdleHoldSeconds = 0.3f;

        [Tooltip("两次切层最小间隔（秒）— Min seconds between layer switches")]
        public float equipLayerSwitchCooldown = 0.4f;

        [Tooltip("判定停下（滞回）：阈值 = movingSpeedThreshold × 此值")]
        [Range(0.1f, 0.9f)]
        public float equipLayerSwitchIdleSpeedMultiplier = 0.5f;

        [Tooltip("代码判定播完的 normalizedTime 阈值 — End detection threshold")]
        [Range(0.8f, 1f)]
        public float equipPlaybackEndNormalizedTime = 0.95f;

        [Header("Weapon Overlay Fade Out / 装备层收尾淡出")]
        [Tooltip("Overlay 层 Empty 状态名 — Empty state name on overlay layers")]
        public string overlayEmptyStateName = "Empty";

        [Tooltip("CrossFade 到 Empty 的时长（秒）— CrossFade to Empty duration")]
        public float overlayEmptyCrossFadeDuration = 0.1f;

        [Tooltip("层权重 1→0 淡出时长（秒）— Layer weight fade-out duration")]
        public float overlayFadeOutDuration = 0.15f;

        [Tooltip("装备/收回+淡出总时长上限（秒），超时强制复位 — Max overlay phase duration before force reset")]
        public float weaponOverlayMaxPhaseSeconds = 8f;

        [Header("Weapon Equip Locomotion Sync / 装备播片与移动同步")]
        [Tooltip("播片期间不按实时速度写 Moving，避免 Base 腿与 overlay 抢权 — Suppress live Moving during overlay")]
        public bool suppressLiveMovingDuringWeaponPlayback = true;

        [Tooltip("勾选则 Equipped 推迟到 overlay 淡出；不勾选则在抓剑/背剑帧立刻写 Equipped — Defer Equipped bool until overlay fade-out")]
        public bool deferEquippedBoolUntilOverlayFadeOut = false;

        [Header("Recovery / 起身")]
        [Tooltip("仰躺起身状态名（通常在 FullBodyLayer）— Get-up-from-back state name")]
        public string getUpBackStateName = "GetUpBack";

        [Tooltip("俯卧起身状态名（通常在 FullBodyLayer）— Get-up-from-front state name")]
        public string getUpFrontStateName = "GetUpFront";

        [Tooltip("起身状态 CrossFade 时长（秒）— CrossFade duration for get-up states")]
        public float getUpCrossFadeDuration = 0.12f;

        [Tooltip("仰躺起身目标播完时长（秒）— Target real-time duration to finish GetUpBack")]
        public float getUpBackTargetDuration = 1.05f;

        [Tooltip("俯卧起身目标播完时长（秒）— Target real-time duration to finish GetUpFront")]
        public float getUpFrontTargetDuration = 1.35f;

        [Tooltip("起身是否使用 FullBodyLayer 播放；关闭则走 Base Layer（推荐）— Use FullBodyLayer for get-up playback; off = Base Layer")]
        public bool recoveryUseFullBodyLayer = false;

        [Tooltip("布娃娃终态到起身首帧的姿态匹配时长（秒）— Pose-match duration from ragdoll settle to get-up first frame")]
        public float recoveryPoseMatchDuration = 0.28f;

        [Tooltip("姿态匹配插值曲线（0~1）— Pose-match blend curve (0~1)")]
        public AnimationCurve recoveryPoseMatchCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("单骨骼最大允许对齐角度（度）；0=不限制 — Max per-bone alignment angle in degrees; 0 = no clamp")]
        public float recoveryMaxBoneAngleClamp = 85f;
    }
}
