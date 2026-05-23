namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 角色控制器状态枚举
    /// Character controller state enumeration
    /// </summary>
    public enum CharacterState
    {
        /// <summary>待机 / 奔跑 — Idle / run locomotion</summary>
        Locomotion,

        /// <summary>拔刀/收刀 overlay 播片（Grounded 子态）— Weapon equip overlay playback</summary>
        WeaponEquipPlayback,

        /// <summary>轻击反应 — Light hit flinch</summary>
        LightFlinch,

        /// <summary>重击踉跄 — Heavy hit stagger with partial ragdoll</summary>
        HeavyStagger,

        /// <summary>平衡值耗尽击倒 — Balance depleted knockdown</summary>
        Knockdown,

        /// <summary>强制击倒（滚石等）— Forced knockdown (boulder, etc.)</summary>
        ForcedKnockdown,

        /// <summary>起身 — Recovery get-up</summary>
        Recovering
    }
}
