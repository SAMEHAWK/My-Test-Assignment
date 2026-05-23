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
    }
}
