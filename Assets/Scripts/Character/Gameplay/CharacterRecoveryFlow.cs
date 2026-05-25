using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 起身类型：仰躺起身 / 俯卧起身
    /// Get-up type: from back / from front
    /// </summary>
    public enum RecoveryGetUpType
    {
        Back,
        Front
    }

    /// <summary>
    /// 击倒后起身（C6 实现姿态匹配与 GetUp 动画）
    /// Recovery after knockdown (pose match + get-up in C6)
    /// </summary>
    public sealed class CharacterRecoveryFlow : ICharacterModule
    {
        float _recoverTimer;
        float _fallbackRecoverDuration;
        bool _hasActiveRecovery;
        bool _isGetUpFinished;
        RecoveryGetUpType _currentGetUpType;
        const float PlaceholderRecoverDuration = 2f;

        public bool IsComplete =>
            _hasActiveRecovery && (_isGetUpFinished || _recoverTimer >= _fallbackRecoverDuration);

        public RecoveryGetUpType CurrentGetUpType => _currentGetUpType;

        public CharacterRecoveryFlow() { }

        public void BeginRecovery(RecoveryGetUpType getUpType, float fallbackRecoverDuration = PlaceholderRecoverDuration)
        {
            _currentGetUpType = getUpType;
            _recoverTimer = 0f;
            _fallbackRecoverDuration = Mathf.Max(0.1f, fallbackRecoverDuration);
            _hasActiveRecovery = true;
            _isGetUpFinished = false;
        }

        public void MarkGetUpFinished() => _isGetUpFinished = true;

        public void OnEnterState(CharacterState state, in HitContext hitContext)
        {
            if (state == CharacterState.Recovering)
            {
                // 兜底：若外部未显式 BeginRecovery，固定使用 Back 保证流程可继续
                // Fallback: if BeginRecovery was not called, force Back to keep flow running
                if (!_hasActiveRecovery)
                    BeginRecovery(RecoveryGetUpType.Back);
            }
        }

        public void OnExitState(CharacterState state)
        {
            if (state == CharacterState.Recovering)
            {
                _hasActiveRecovery = false;
                _isGetUpFinished = false;
            }
        }

        public void OnTickState(CharacterState state, float deltaTime)
        {
            if (state == CharacterState.Recovering)
                _recoverTimer += deltaTime;
        }

        public void OnFixedTickState(CharacterState state, float fixedDeltaTime) { }
    }
}
