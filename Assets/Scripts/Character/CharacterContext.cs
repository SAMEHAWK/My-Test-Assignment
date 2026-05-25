namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 角色运行时快照（供模块与调试只读）
    /// Runtime character snapshot for modules and debugging
    /// </summary>
    public sealed class CharacterContext
    {
        public CharacterState State { get; internal set; }
        public CharacterSuperstate Superstate { get; internal set; }
        public float StateTime { get; internal set; }
        public UnityEngine.Vector3 WorldVelocity { get; internal set; }
        public int CurrentBalance { get; internal set; }
        public int MaxBalance { get; internal set; }
        public HitContext LastHit { get; internal set; }

        /// <summary>
        /// 是否允许移动输入（HSM 能力快照）
        /// Whether locomotion input is allowed this frame
        /// </summary>
        public bool CanMove { get; internal set; }

        /// <summary>
        /// 是否允许发起拔刀/收刀（典型仅 Locomotion）
        /// Whether weapon equip toggle can be initiated
        /// </summary>
        public bool CanToggleWeapon { get; internal set; }

        /// <summary>
        /// 武器是否处于装备显示状态（由动画事件更新）
        /// Whether weapon mesh is shown per animation events
        /// </summary>
        public bool IsWeaponEquipped { get; internal set; }

        /// <summary>
        /// 地面轻击 Flinch overlay（不切 HSM）
        /// Grounded light-hit additive flinch overlay active
        /// </summary>
        public bool IsLightFlinchOverlayActive { get; internal set; }

        /// <summary>
        /// 布娃娃是否满足沉降判定（可进入起身流程）
        /// Whether ragdoll is settled and ready for recovery
        /// </summary>
        public bool IsRagdollSettled { get; internal set; }

        /// <summary>
        /// 当前布娃娃模式（双骨架后端）
        /// Current ragdoll mode from dual-skeleton backend
        /// </summary>
        public CharacterRagdollMode RagdollMode { get; internal set; }

        /// <summary>
        /// 布娃娃后端状态（Dual / Unavailable）
        /// Ragdoll backend status string (Dual / Unavailable)
        /// </summary>
        public string RagdollBackendStatus { get; internal set; } = "Unavailable";

        /// <summary>
        /// 当前命中的链名（无则 None）
        /// Active propagated chain name (None when idle)
        /// </summary>
        public string RagdollChainName { get; internal set; } = "None";

        /// <summary>
        /// 当前映射骨骼数量（Visual ↔ Physics）
        /// Current mapped bone count (Visual ↔ Physics)
        /// </summary>
        public int RagdollMappedBoneCount { get; internal set; }

        /// <summary>
        /// 是否使用双骨架后端
        /// Whether dual-skeleton backend is currently active
        /// </summary>
        public bool IsUsingDualRagdoll { get; internal set; }
    }
}
