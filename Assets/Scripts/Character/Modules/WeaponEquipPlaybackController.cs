using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 装备/收回播放阶段（代码驱动 overlay，不用 Animator Trigger）
    /// Weapon equip playback phase — code-driven overlay, no animator triggers
    /// </summary>
    public enum WeaponPlaybackPhase
    {
        None,
        EquipPlaying,
        UnequipPlaying,
        /// <summary>播片结束：CrossFade 到 Empty 并淡出层权重</summary>
        OverlayFadingOut
    }

    /// <summary>
    /// 武器拔刀/收刀 overlay：层权重、CrossFade、中途 FullBody↔UpBody 切层
    /// Weapon equip/unequip overlay playback — layer weights and mid-playback switches
    /// </summary>
    public sealed class WeaponEquipPlaybackController
    {
        readonly Animator _animator;
        readonly CharacterAnimationConfig _animConfig;

        readonly int _equippedHash;
        readonly int _equipFullBodyStateHash;
        readonly int _equipUpBodyStateHash;
        readonly int _unequipFullBodyStateHash;
        readonly int _unequipUpBodyStateHash;
        readonly int _overlayEmptyStateHash;
        readonly int _upBodyLayerIndex;
        readonly int _fullBodyLayerIndex;

        bool _isWeaponEquipped;
        WeaponPlaybackPhase _playbackPhase;
        int _activeOverlayLayerIndex = -1;
        int _activePlaybackStateHash;
        float _lastNormalizedSpeed;
        bool _playbackUsesUpBodyLayer;
        bool _enteredActivePlaybackState;
        float _trackedPlaybackNormalizedTime;
        float _layerSwitchCooldownRemaining;
        float _moveHoldElapsed;
        float _idleHoldElapsed;
        float _playbackElapsed;
        float _overlayPhaseElapsed;
        float _overlayFadeElapsed;
        float _fadeStartFullBodyWeight;
        float _fadeStartUpBodyWeight;
        float _layerWeightTransitionRemaining;
        float _layerWeightTransitionDuration;
        float _layerWeightFromFull;
        float _layerWeightFromUp;
        float _layerWeightToFull;
        float _layerWeightToUp;

        public bool IsWeaponEquipped => _isWeaponEquipped;

        /// <summary>是否正在播装备/收回或收尾淡出</summary>
        public bool IsEquipPlaybackInProgress => _playbackPhase != WeaponPlaybackPhase.None;

        public float LastNormalizedSpeed => _lastNormalizedSpeed;

        public WeaponEquipPlaybackController(Animator animator, CharacterAnimationConfig animConfig)
        {
            _animator = animator;
            _animConfig = animConfig;

            if (_animConfig != null)
            {
                _equippedHash = Animator.StringToHash(_animConfig.equippedParam);
                _equipFullBodyStateHash = Animator.StringToHash(_animConfig.equipFullBodyStateName);
                _equipUpBodyStateHash = Animator.StringToHash(_animConfig.equipUpBodyStateName);
                _unequipFullBodyStateHash = Animator.StringToHash(_animConfig.unequipFullBodyStateName);
                _unequipUpBodyStateHash = Animator.StringToHash(_animConfig.unequipUpBodyStateName);
                _overlayEmptyStateHash = Animator.StringToHash(_animConfig.overlayEmptyStateName);
            }

            if (_animator != null && _animConfig != null)
            {
                _upBodyLayerIndex = _animator.GetLayerIndex(_animConfig.upBodyLayerName);
                _fullBodyLayerIndex = _animator.GetLayerIndex(_animConfig.fullBodyLayerName);

                if (_upBodyLayerIndex < 0)
                {
                    Debug.LogWarning(
                        $"[WeaponEquipPlaybackController] Layer not found: {_animConfig.upBodyLayerName}",
                        _animator);
                }

                if (_fullBodyLayerIndex < 0)
                {
                    Debug.LogWarning(
                        $"[WeaponEquipPlaybackController] Layer not found: {_animConfig.fullBodyLayerName}",
                        _animator);
                }
            }

            ResetEquipOverlayLayerWeights();
            SetEquippedAnimator(false);
        }

        public void SetLastNormalizedSpeed(float normalizedSpeed) => _lastNormalizedSpeed = normalizedSpeed;

        /// <summary>
        /// 播片期间 Moving 锁定策略（供 CharacterAnimationPresenter.SyncLocomotion 查询）
        /// Moving bool policy during overlay playback
        /// </summary>
        public bool ResolveLocomotionMovingBool(float normalizedSpeed)
        {
            if (_animConfig == null)
                return normalizedSpeed >= 0.1f;

            if (!_animConfig.suppressLiveMovingDuringWeaponPlayback || !IsEquipPlaybackInProgress)
                return normalizedSpeed >= _animConfig.movingSpeedThreshold;

            if (_playbackPhase == WeaponPlaybackPhase.OverlayFadingOut)
                return normalizedSpeed >= _animConfig.movingSpeedThreshold;

            return _playbackUsesUpBodyLayer;
        }

        public bool TryBeginToggle(bool movingAtPress) =>
            TryBeginToggle(movingAtPress, _lastNormalizedSpeed);

        public bool TryBeginToggle(bool movingAtPress, float normalizedSpeedAtPress)
        {
            if (_animator == null || _animConfig == null)
                return false;

            _lastNormalizedSpeed = normalizedSpeedAtPress;

            if (IsEquipPlaybackInProgress)
            {
                Tick(Time.deltaTime);
                if (IsEquipPlaybackInProgress)
                    return false;
            }

            var isUnequip = _isWeaponEquipped;

            if (!TryResolvePlaybackTarget(movingAtPress, isUnequip, out var layerIndex, out var stateHash))
                return false;

            _playbackPhase = isUnequip ? WeaponPlaybackPhase.UnequipPlaying : WeaponPlaybackPhase.EquipPlaying;
            _activeOverlayLayerIndex = layerIndex;
            _activePlaybackStateHash = stateHash;
            _playbackUsesUpBodyLayer = movingAtPress;
            _enteredActivePlaybackState = false;
            _trackedPlaybackNormalizedTime = 0f;
            _layerSwitchCooldownRemaining = 0f;
            _moveHoldElapsed = 0f;
            _idleHoldElapsed = 0f;
            _playbackElapsed = 0f;
            _overlayPhaseElapsed = 0f;
            _layerWeightTransitionRemaining = 0f;

            ApplyEquipPlaybackLayerWeights(movingAtPress);
            _animator.CrossFadeInFixedTime(stateHash, _animConfig.equipCrossFadeDuration, layerIndex, 0f);
            return true;
        }

        public void SetWeaponEquippedState(bool equipped)
        {
            _isWeaponEquipped = equipped;

            if (_animConfig == null || !_animConfig.deferEquippedBoolUntilOverlayFadeOut || !IsEquipPlaybackInProgress)
                SetEquippedAnimator(equipped);
        }

        public void CompleteWeaponEquipPlayback()
        {
            if (_playbackPhase == WeaponPlaybackPhase.None)
            {
                ForceResetOverlayLayerWeightsIfNonZero();
                return;
            }

            BeginOverlayFadeOut();
        }

        public void SetEquippedAnimator(bool equipped)
        {
            if (_animator == null || _animConfig == null)
                return;

            _animator.SetBool(_equippedHash, equipped);
        }

        public void Abort() => ForceAbortWeaponOverlayPlayback();

        public void Tick(float deltaTime)
        {
            TickWeaponPlaybackWatchdog(deltaTime);
            TickOverlayFadeOut(deltaTime);
            TickUpdateEnteredActivePlaybackState();
            TickTrackPlaybackNormalizedTime();
            TickWeaponEquipLayerSwitchByMovement(deltaTime);
            TickLayerWeightTransition(deltaTime);
            EnforcePlaybackLayerWeights();
            TickWeaponEquipAnimatorLeftPlaybackState();
            TickWeaponEquipPlaybackCompletion(deltaTime);
        }

        bool TryResolvePlaybackTarget(bool moving, bool isUnequip, out int layerIndex, out int stateHash)
        {
            layerIndex = -1;
            stateHash = 0;

            if (moving)
            {
                layerIndex = _upBodyLayerIndex;
                stateHash = isUnequip ? _unequipUpBodyStateHash : _equipUpBodyStateHash;
            }
            else
            {
                layerIndex = _fullBodyLayerIndex;
                stateHash = isUnequip ? _unequipFullBodyStateHash : _equipFullBodyStateHash;
            }

            if (layerIndex < 0)
            {
                Debug.LogWarning(
                    "[WeaponEquipPlaybackController] Overlay layer index invalid — check Config layer names",
                    _animator);
                return false;
            }

            return true;
        }

        float GetLayerSwitchCrossFadeSeconds() =>
            _animConfig != null ? Mathf.Max(_animConfig.equipMovingLayerSwitchCrossFade, 0.01f) : 0.1f;

        void ApplyEquipPlaybackLayerWeights(bool useUpBodyLayer)
        {
            if (_animator == null)
                return;

            var fullWeight = useUpBodyLayer ? 0f : 1f;
            var upWeight = useUpBodyLayer ? 1f : 0f;

            if (_fullBodyLayerIndex >= 0)
                _animator.SetLayerWeight(_fullBodyLayerIndex, fullWeight);

            if (_upBodyLayerIndex >= 0)
                _animator.SetLayerWeight(_upBodyLayerIndex, upWeight);
        }

        void BeginLayerWeightTransition(bool useUpBodyLayer)
        {
            if (_animator == null)
                return;

            _layerWeightTransitionDuration = GetLayerSwitchCrossFadeSeconds();
            _layerWeightTransitionRemaining = _layerWeightTransitionDuration;

            _layerWeightFromFull = _fullBodyLayerIndex >= 0 ? _animator.GetLayerWeight(_fullBodyLayerIndex) : 0f;
            _layerWeightFromUp = _upBodyLayerIndex >= 0 ? _animator.GetLayerWeight(_upBodyLayerIndex) : 0f;
            _layerWeightToFull = useUpBodyLayer ? 0f : 1f;
            _layerWeightToUp = useUpBodyLayer ? 1f : 0f;
        }

        void ResetEquipOverlayLayerWeights()
        {
            if (_animator == null)
                return;

            if (_fullBodyLayerIndex >= 0)
                _animator.SetLayerWeight(_fullBodyLayerIndex, 0f);

            if (_upBodyLayerIndex >= 0)
                _animator.SetLayerWeight(_upBodyLayerIndex, 0f);
        }

        void BeginOverlayFadeOut()
        {
            if (_playbackPhase == WeaponPlaybackPhase.OverlayFadingOut)
                return;

            if (_animator == null || _animConfig == null)
            {
                _playbackPhase = WeaponPlaybackPhase.None;
                _activeOverlayLayerIndex = -1;
                ResetEquipOverlayLayerWeights();
                return;
            }

            _fadeStartFullBodyWeight = _fullBodyLayerIndex >= 0 ? _animator.GetLayerWeight(_fullBodyLayerIndex) : 0f;
            _fadeStartUpBodyWeight = _upBodyLayerIndex >= 0 ? _animator.GetLayerWeight(_upBodyLayerIndex) : 0f;
            _overlayFadeElapsed = 0f;
            _playbackPhase = WeaponPlaybackPhase.OverlayFadingOut;

            if (_activeOverlayLayerIndex >= 0)
            {
                _animator.CrossFadeInFixedTime(
                    _overlayEmptyStateHash,
                    _animConfig.overlayEmptyCrossFadeDuration,
                    _activeOverlayLayerIndex,
                    0f);
            }
        }

        void FinishOverlayFadeOut()
        {
            _playbackPhase = WeaponPlaybackPhase.None;
            _activeOverlayLayerIndex = -1;
            _activePlaybackStateHash = 0;
            _playbackUsesUpBodyLayer = false;
            _enteredActivePlaybackState = false;
            _trackedPlaybackNormalizedTime = 0f;
            _layerSwitchCooldownRemaining = 0f;
            _moveHoldElapsed = 0f;
            _idleHoldElapsed = 0f;
            _overlayPhaseElapsed = 0f;
            _overlayFadeElapsed = 0f;
            _layerWeightTransitionRemaining = 0f;
            ResetEquipOverlayLayerWeights();
            SyncEquippedAnimatorAfterOverlay();
        }

        void SyncEquippedAnimatorAfterOverlay() => SetEquippedAnimator(_isWeaponEquipped);

        void ForceAbortWeaponOverlayPlayback()
        {
            _playbackPhase = WeaponPlaybackPhase.None;
            _activeOverlayLayerIndex = -1;
            _activePlaybackStateHash = 0;
            _playbackUsesUpBodyLayer = false;
            _enteredActivePlaybackState = false;
            _trackedPlaybackNormalizedTime = 0f;
            _layerSwitchCooldownRemaining = 0f;
            _moveHoldElapsed = 0f;
            _idleHoldElapsed = 0f;
            _playbackElapsed = 0f;
            _overlayPhaseElapsed = 0f;
            _overlayFadeElapsed = 0f;
            _layerWeightTransitionRemaining = 0f;
            ResetEquipOverlayLayerWeights();
            SyncEquippedAnimatorAfterOverlay();
        }

        bool IsPlaybackMovingNow() =>
            _animConfig != null && _lastNormalizedSpeed >= _animConfig.movingSpeedThreshold;

        bool IsPlaybackIdleNow()
        {
            if (_animConfig == null)
                return true;

            var mult = Mathf.Clamp(_animConfig.equipLayerSwitchIdleSpeedMultiplier, 0.1f, 0.9f);
            return _lastNormalizedSpeed < _animConfig.movingSpeedThreshold * mult;
        }

        float GetLayerSwitchCooldownSeconds() =>
            _animConfig != null ? Mathf.Max(_animConfig.equipLayerSwitchCooldown, 0.1f) : 0.4f;

        static float GetStateNormalizedTime(AnimatorStateInfo info) =>
            info.loop ? Mathf.Repeat(info.normalizedTime, 1f) : Mathf.Clamp01(info.normalizedTime);

        void TickTrackPlaybackNormalizedTime()
        {
            if (_playbackPhase != WeaponPlaybackPhase.EquipPlaying &&
                _playbackPhase != WeaponPlaybackPhase.UnequipPlaying)
                return;

            if (_animator == null || _activeOverlayLayerIndex < 0 || _activePlaybackStateHash == 0)
                return;

            if (_animator.IsInTransition(_activeOverlayLayerIndex))
                return;

            var info = _animator.GetCurrentAnimatorStateInfo(_activeOverlayLayerIndex);
            if (info.shortNameHash != _activePlaybackStateHash)
                return;

            _trackedPlaybackNormalizedTime = GetStateNormalizedTime(info);
        }

        float GetActiveOverlayClipLengthSeconds()
        {
            if (_animator == null || _activeOverlayLayerIndex < 0)
                return 2.5f;

            var info = _animator.GetCurrentAnimatorStateInfo(_activeOverlayLayerIndex);
            return Mathf.Max(info.length, 0.01f);
        }

        void CrossFadeOverlayStateAtTrackedTime(int layerIndex, int stateHash)
        {
            if (_animator == null || layerIndex < 0 || _animConfig == null)
                return;

            var t = Mathf.Clamp01(_trackedPlaybackNormalizedTime);
            var clipLength = GetActiveOverlayClipLengthSeconds();
            var transitionSeconds = GetLayerSwitchCrossFadeSeconds();
            var normalizedTransition = Mathf.Clamp01(transitionSeconds / clipLength);
            _animator.CrossFade(stateHash, normalizedTransition, layerIndex, t);
        }

        void TickUpdateEnteredActivePlaybackState()
        {
            if (_playbackPhase != WeaponPlaybackPhase.EquipPlaying &&
                _playbackPhase != WeaponPlaybackPhase.UnequipPlaying)
                return;

            if (_animator == null || _activeOverlayLayerIndex < 0 || _activePlaybackStateHash == 0)
                return;

            if (_animator.IsInTransition(_activeOverlayLayerIndex))
                return;

            var info = _animator.GetCurrentAnimatorStateInfo(_activeOverlayLayerIndex);
            if (info.shortNameHash == _activePlaybackStateHash)
                _enteredActivePlaybackState = true;
        }

        void EnforcePlaybackLayerWeights()
        {
            if (_playbackPhase != WeaponPlaybackPhase.EquipPlaying &&
                _playbackPhase != WeaponPlaybackPhase.UnequipPlaying)
                return;

            if (_layerWeightTransitionRemaining > 0f)
                return;

            ApplyEquipPlaybackLayerWeights(_playbackUsesUpBodyLayer);
        }

        void TickLayerWeightTransition(float deltaTime)
        {
            if (_layerWeightTransitionRemaining <= 0f || _animator == null)
                return;

            _layerWeightTransitionRemaining -= deltaTime;
            var duration = Mathf.Max(_layerWeightTransitionDuration, 0.001f);
            var t = 1f - Mathf.Clamp01(_layerWeightTransitionRemaining / duration);

            if (_fullBodyLayerIndex >= 0)
                _animator.SetLayerWeight(_fullBodyLayerIndex, Mathf.Lerp(_layerWeightFromFull, _layerWeightToFull, t));

            if (_upBodyLayerIndex >= 0)
                _animator.SetLayerWeight(_upBodyLayerIndex, Mathf.Lerp(_layerWeightFromUp, _layerWeightToUp, t));

            if (_layerWeightTransitionRemaining <= 0f)
                ApplyEquipPlaybackLayerWeights(_playbackUsesUpBodyLayer);
        }

        void TickWeaponEquipLayerSwitchByMovement(float deltaTime)
        {
            if (_playbackPhase != WeaponPlaybackPhase.EquipPlaying &&
                _playbackPhase != WeaponPlaybackPhase.UnequipPlaying)
                return;

            if (_animator == null || _animConfig == null || !_enteredActivePlaybackState)
                return;

            if (_layerSwitchCooldownRemaining > 0f)
                _layerSwitchCooldownRemaining -= deltaTime;

            if (_layerSwitchCooldownRemaining > 0f)
                return;

            if (_animator.IsInTransition(_activeOverlayLayerIndex))
                return;

            if (IsPlaybackMovingNow())
            {
                _moveHoldElapsed += deltaTime;
                _idleHoldElapsed = 0f;
            }
            else if (IsPlaybackIdleNow())
            {
                _idleHoldElapsed += deltaTime;
                _moveHoldElapsed = 0f;
            }
            else
            {
                _moveHoldElapsed = 0f;
                _idleHoldElapsed = 0f;
            }

            var isUnequip = _playbackPhase == WeaponPlaybackPhase.UnequipPlaying;
            var moveHold = Mathf.Max(_animConfig.equipLayerSwitchMoveHoldSeconds, 0.05f);
            var idleHold = Mathf.Max(_animConfig.equipLayerSwitchIdleHoldSeconds, 0.05f);

            if (!_playbackUsesUpBodyLayer && _moveHoldElapsed >= moveHold)
                SwitchPlaybackToUpBodyLayer(isUnequip);
            else if (_playbackUsesUpBodyLayer && _idleHoldElapsed >= idleHold)
                SwitchPlaybackToFullBodyLayer(isUnequip);
        }

        void SwitchPlaybackToUpBodyLayer(bool isUnequip)
        {
            if (_fullBodyLayerIndex < 0 || _upBodyLayerIndex < 0)
                return;

            var targetHash = isUnequip ? _unequipUpBodyStateHash : _equipUpBodyStateHash;
            CrossFadeOverlayStateAtTrackedTime(_upBodyLayerIndex, targetHash);

            _activeOverlayLayerIndex = _upBodyLayerIndex;
            _activePlaybackStateHash = targetHash;
            _playbackUsesUpBodyLayer = true;
            _enteredActivePlaybackState = true;
            _layerSwitchCooldownRemaining = GetLayerSwitchCooldownSeconds();
            _moveHoldElapsed = 0f;
            _idleHoldElapsed = 0f;

            BeginLayerWeightTransition(true);
        }

        void SwitchPlaybackToFullBodyLayer(bool isUnequip)
        {
            if (_fullBodyLayerIndex < 0 || _upBodyLayerIndex < 0)
                return;

            var targetHash = isUnequip ? _unequipFullBodyStateHash : _equipFullBodyStateHash;
            CrossFadeOverlayStateAtTrackedTime(_fullBodyLayerIndex, targetHash);

            _activeOverlayLayerIndex = _fullBodyLayerIndex;
            _activePlaybackStateHash = targetHash;
            _playbackUsesUpBodyLayer = false;
            _enteredActivePlaybackState = true;
            _layerSwitchCooldownRemaining = GetLayerSwitchCooldownSeconds();
            _moveHoldElapsed = 0f;
            _idleHoldElapsed = 0f;

            BeginLayerWeightTransition(false);
        }

        void ForceResetOverlayLayerWeightsIfNonZero()
        {
            if (_animator == null)
                return;

            var needsReset = false;
            if (_fullBodyLayerIndex >= 0 && _animator.GetLayerWeight(_fullBodyLayerIndex) > 0.001f)
                needsReset = true;
            if (_upBodyLayerIndex >= 0 && _animator.GetLayerWeight(_upBodyLayerIndex) > 0.001f)
                needsReset = true;

            if (needsReset)
                ResetEquipOverlayLayerWeights();
        }

        void TickOverlayFadeOut(float deltaTime)
        {
            if (_playbackPhase != WeaponPlaybackPhase.OverlayFadingOut || _animator == null || _animConfig == null)
                return;

            var duration = Mathf.Max(_animConfig.overlayFadeOutDuration, 0.01f);
            _overlayFadeElapsed += deltaTime;
            var t = Mathf.Clamp01(_overlayFadeElapsed / duration);

            if (_fullBodyLayerIndex >= 0)
                _animator.SetLayerWeight(_fullBodyLayerIndex, Mathf.Lerp(_fadeStartFullBodyWeight, 0f, t));

            if (_upBodyLayerIndex >= 0)
                _animator.SetLayerWeight(_upBodyLayerIndex, Mathf.Lerp(_fadeStartUpBodyWeight, 0f, t));

            if (t >= 1f)
                FinishOverlayFadeOut();
        }

        void TickWeaponPlaybackWatchdog(float deltaTime)
        {
            if (!IsEquipPlaybackInProgress || _animConfig == null)
                return;

            _overlayPhaseElapsed += deltaTime;
            var maxPhase = Mathf.Max(_animConfig.weaponOverlayMaxPhaseSeconds, 0.5f);

            if (_playbackPhase == WeaponPlaybackPhase.OverlayFadingOut)
            {
                var fadeLimit = Mathf.Max(_animConfig.overlayFadeOutDuration, 0.01f) + 0.25f;
                if (_overlayFadeElapsed >= fadeLimit)
                    FinishOverlayFadeOut();
            }

            if (_overlayPhaseElapsed < maxPhase)
                return;

#if UNITY_EDITOR
            Debug.LogWarning(
                "[WeaponEquipPlaybackController] Weapon overlay timed out — forcing reset",
                _animator);
#endif
            ForceAbortWeaponOverlayPlayback();
        }

        void TickWeaponEquipAnimatorLeftPlaybackState()
        {
            if (_playbackPhase != WeaponPlaybackPhase.EquipPlaying &&
                _playbackPhase != WeaponPlaybackPhase.UnequipPlaying)
                return;

            if (_animator == null || _animConfig == null || _activeOverlayLayerIndex < 0)
                return;

            if (_animator.IsInTransition(_activeOverlayLayerIndex) || !_enteredActivePlaybackState)
                return;

            if (_layerSwitchCooldownRemaining > 0f)
                return;

            var minPlayDuration = Mathf.Max(_animConfig.equipCrossFadeDuration, 0.1f);
            if (_playbackElapsed < minPlayDuration)
                return;

            var info = _animator.GetCurrentAnimatorStateInfo(_activeOverlayLayerIndex);
            if (info.shortNameHash == _activePlaybackStateHash)
                return;

            CompleteWeaponEquipPlayback();
        }

        void TickWeaponEquipPlaybackCompletion(float deltaTime)
        {
            if (_playbackPhase != WeaponPlaybackPhase.EquipPlaying &&
                _playbackPhase != WeaponPlaybackPhase.UnequipPlaying)
                return;

            if (_animator == null || _animConfig == null || _activeOverlayLayerIndex < 0 || _activePlaybackStateHash == 0)
                return;

            _playbackElapsed += deltaTime;

            if (_animator.IsInTransition(_activeOverlayLayerIndex))
                return;

            var minPlayDuration = Mathf.Max(_animConfig.equipCrossFadeDuration, 0.1f);
            if (_playbackElapsed < minPlayDuration)
                return;

            var info = _animator.GetCurrentAnimatorStateInfo(_activeOverlayLayerIndex);
            if (info.shortNameHash == _activePlaybackStateHash)
            {
                _enteredActivePlaybackState = true;
                _trackedPlaybackNormalizedTime = GetStateNormalizedTime(info);
            }

            if (info.shortNameHash != _activePlaybackStateHash)
                return;

            if (_layerSwitchCooldownRemaining > 0f)
                return;

            if (info.normalizedTime < _animConfig.equipPlaybackEndNormalizedTime)
                return;

            CompleteWeaponEquipPlayback();
        }
    }
}
