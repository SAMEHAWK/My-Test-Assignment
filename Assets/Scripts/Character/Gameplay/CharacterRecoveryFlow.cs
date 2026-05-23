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
        readonly Transform _faceProbeTransform;
        readonly Transform _fallbackSpineTransform;
        readonly float _faceUpDotThreshold;

        float _recoverTimer;
        float _fallbackRecoverDuration;
        bool _hasActiveRecovery;
        bool _isGetUpFinished;
        RecoveryGetUpType _currentGetUpType;
        const float PlaceholderRecoverDuration = 2f;

        public bool IsComplete =>
            _hasActiveRecovery && (_isGetUpFinished || _recoverTimer >= _fallbackRecoverDuration);

        public RecoveryGetUpType CurrentGetUpType => _currentGetUpType;

        public CharacterRecoveryFlow(Transform faceProbeTransform, Transform fallbackSpineTransform, float faceUpDotThreshold)
        {
            _faceProbeTransform = faceProbeTransform;
            _fallbackSpineTransform = fallbackSpineTransform;
            _faceUpDotThreshold = faceUpDotThreshold;
        }

        public void BeginRecovery(RecoveryGetUpType getUpType, float fallbackRecoverDuration = PlaceholderRecoverDuration)
        {
            _currentGetUpType = getUpType;
            _recoverTimer = 0f;
            _fallbackRecoverDuration = Mathf.Max(0.1f, fallbackRecoverDuration);
            _hasActiveRecovery = true;
            _isGetUpFinished = false;
        }

        public void MarkGetUpFinished() => _isGetUpFinished = true;

        public bool IsFaceUp()
        {
            if (_faceProbeTransform == null && _fallbackSpineTransform == null)
                return true;

            // 优先用胸口朝向轴（forward）判定仰/俯：朝上=仰躺，朝下=俯卧
            // Prefer torso forward axis for face-up/down classification
            if (_faceProbeTransform != null)
            {
                var forwardDot = Vector3.Dot(_faceProbeTransform.forward, Vector3.up);
                if (Mathf.Abs(forwardDot) > 0.05f)
                    return forwardDot > _faceUpDotThreshold;
            }

            // 兜底：旧逻辑兼容（某些模型可能未配置 torso facing）
            // Fallback: legacy spine-up heuristic for compatibility
            if (_fallbackSpineTransform != null)
                return Vector3.Dot(_fallbackSpineTransform.up, Vector3.up) > _faceUpDotThreshold;

            return true;
        }

        public RecoveryGetUpType EvaluateGetUpType() =>
            IsFaceUp() ? RecoveryGetUpType.Back : RecoveryGetUpType.Front;

        public void OnEnterState(CharacterState state, in HitContext hitContext)
        {
            if (state == CharacterState.Recovering)
            {
                // 兜底：若外部未显式 BeginRecovery，按当前朝向自动开始
                // Fallback: auto-begin by current facing if not explicitly started
                if (!_hasActiveRecovery)
                    BeginRecovery(EvaluateGetUpType());
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
