namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 角色控制器子模块接口 — 仅由 Root 调度
    /// Character controller sub-module — dispatched only by root
    /// </summary>
    public interface ICharacterModule
    {
        /// <summary>
        /// 进入状态时调用
        /// Called when entering a state
        /// </summary>
        void OnEnterState(CharacterState state, in HitContext hitContext);

        /// <summary>
        /// 离开状态时调用
        /// Called when exiting a state
        /// </summary>
        void OnExitState(CharacterState state);

        /// <summary>
        /// 每帧逻辑更新（Update）
        /// Per-frame logic tick (Update)
        /// </summary>
        void OnTickState(CharacterState state, float deltaTime);

        /// <summary>
        /// 物理更新（FixedUpdate），不需要物理的模块可留空
        /// Physics tick (FixedUpdate); no-op if module has no physics
        /// </summary>
        void OnFixedTickState(CharacterState state, float fixedDeltaTime);
    }
}
