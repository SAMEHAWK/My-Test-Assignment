using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 主动攻击 overlay 播放控制：攻击层权重、状态切换与兜底结束
    /// Player attack overlay playback: layer weight, state switching, and fallback completion
    /// </summary>
    public sealed class CharacterAttackPlaybackController
    {
        enum AttackPlaybackPhase
        {
            None,
            Playing,
            FadingOut
        }

        readonly Animator _animator;
        readonly CharacterAnimationConfig _animConfig;
        readonly int _attackLayerIndex = -1;
        readonly int _attackLightStateHash;
        readonly int _attackHeavyStateHash;
        readonly int _overlayEmptyStateHash;

        AttackPlaybackPhase _phase;
        float _playbackElapsed;
        float _fadeElapsed;
        float _fadeStartWeight;

        public bool IsAttackPlaybackInProgress => _phase != AttackPlaybackPhase.None;

        public CharacterAttackPlaybackController(Animator animator, CharacterAnimationConfig animConfig)
        {
            _animator = animator;
            _animConfig = animConfig;

            if (_animConfig != null)
            {
                _attackLightStateHash = Animator.StringToHash(_animConfig.attackLightStateName);
                _attackHeavyStateHash = Animator.StringToHash(_animConfig.attackHeavyStateName);
                _overlayEmptyStateHash = Animator.StringToHash(_animConfig.overlayEmptyStateName);
            }

            if (_animator != null && _animConfig != null && !string.IsNullOrEmpty(_animConfig.attackLayerName))
            {
                _attackLayerIndex = _animator.GetLayerIndex(_animConfig.attackLayerName);
                if (_attackLayerIndex < 0)
                {
                    Debug.LogWarning(
                        $"[CharacterAttackPlaybackController] 未找到攻击层: {_animConfig.attackLayerName}\n" +
                        $"[CharacterAttackPlaybackController] Attack layer not found: {_animConfig.attackLayerName}",
                        _animator);
                }
            }

            ResetLayerWeight();
        }

        public bool TryBeginAttack(CharacterAttackType attackType)
        {
            if (_animator == null || _animConfig == null || _attackLayerIndex < 0)
                return false;

            var stateHash = ResolveAttackStateHash(attackType);
            if (stateHash == 0 || !_animator.HasState(_attackLayerIndex, stateHash))
                return false;

            _phase = AttackPlaybackPhase.Playing;
            _playbackElapsed = 0f;
            _fadeElapsed = 0f;
            _fadeStartWeight = 1f;

            _animator.SetLayerWeight(_attackLayerIndex, 1f);
            _animator.CrossFadeInFixedTime(
                stateHash,
                Mathf.Max(0f, _animConfig.attackCrossFadeDuration),
                _attackLayerIndex,
                0f);
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (_phase == AttackPlaybackPhase.None || _animator == null || _animConfig == null || _attackLayerIndex < 0)
                return;

            var dt = Mathf.Max(0f, deltaTime);
            if (_phase == AttackPlaybackPhase.Playing)
            {
                _playbackElapsed += dt;
                if (_playbackElapsed >= Mathf.Max(0.01f, _animConfig.attackFallbackDuration))
                    CompleteAttackPlayback();
                return;
            }

            _fadeElapsed += dt;
            var duration = Mathf.Max(0.01f, _animConfig.attackOverlayFadeOutDuration);
            var weight = Mathf.Lerp(_fadeStartWeight, 0f, Mathf.Clamp01(_fadeElapsed / duration));
            _animator.SetLayerWeight(_attackLayerIndex, weight);
            if (weight <= 0.001f)
                FinishFadeOut();
        }

        public void CompleteAttackPlayback()
        {
            if (_phase == AttackPlaybackPhase.None)
                return;

            _phase = AttackPlaybackPhase.FadingOut;
            _fadeElapsed = 0f;
            _fadeStartWeight = _animator != null && _attackLayerIndex >= 0
                ? _animator.GetLayerWeight(_attackLayerIndex)
                : 0f;

            if (_animator != null && _attackLayerIndex >= 0 && _overlayEmptyStateHash != 0 && _animator.HasState(_attackLayerIndex, _overlayEmptyStateHash))
            {
                _animator.CrossFadeInFixedTime(
                    _overlayEmptyStateHash,
                    Mathf.Max(0f, _animConfig.attackOverlayFadeOutDuration),
                    _attackLayerIndex,
                    0f);
            }
        }

        public void Abort()
        {
            _phase = AttackPlaybackPhase.None;
            _playbackElapsed = 0f;
            _fadeElapsed = 0f;
            _fadeStartWeight = 0f;
            ResetLayerWeight();
        }

        int ResolveAttackStateHash(CharacterAttackType attackType) =>
            attackType == CharacterAttackType.Heavy ? _attackHeavyStateHash : _attackLightStateHash;

        void FinishFadeOut()
        {
            _phase = AttackPlaybackPhase.None;
            _playbackElapsed = 0f;
            _fadeElapsed = 0f;
            _fadeStartWeight = 0f;
            ResetLayerWeight();
        }

        void ResetLayerWeight()
        {
            if (_animator != null && _attackLayerIndex >= 0)
                _animator.SetLayerWeight(_attackLayerIndex, 0f);
        }
    }
}
