using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 动画门面：Locomotion、轻击 Flinch overlay、程序化脊柱/颈/头弯曲、武器 overlay
    /// Animation facade: locomotion, flinch overlay, procedural spine/neck/head recoil, weapon overlay
    /// </summary>
    public sealed class CharacterAnimationPresenter : ICharacterModule
    {
        /// <summary>
        /// 单根骨骼的轻击反冲状态
        /// Per-bone light-hit recoil state
        /// </summary>
        sealed class BoneRecoilSlot
        {
            public Transform Bone;
            public bool Pending;
            public bool Active;
            public Quaternion RecoilOffsetRotation = Quaternion.identity;
            public Quaternion LastAppliedOffset = Quaternion.identity;
            public bool HasLastApplied;
        }

        readonly Animator _animator;
        readonly Transform _spineTransform;
        readonly Transform _torsoFacingTransform;
        readonly Transform _neckRecoilTransform;
        readonly Transform _headRecoilTransform;
        readonly CharacterControllerConfig _controllerConfig;
        readonly CharacterAnimationConfig _animConfig;
        readonly WeaponEquipPlaybackController _weaponEquip;

        readonly BoneRecoilSlot _spineRecoil = new();
        readonly BoneRecoilSlot _neckRecoil = new();
        readonly BoneRecoilSlot _headRecoil = new();

        readonly int _speedHash;
        readonly int _hitBlendXHash;
        readonly int _hitBlendZHash;
        readonly int _flinchWeightHash;
        readonly int _movingHash;
        readonly int _flinchLayerIndex = -1;
        readonly int _fullBodyLayerIndex = -1;
        readonly int _getUpBackStateHash;
        readonly int _getUpFrontStateHash;
        readonly int _heavyStaggerStateHash;

        bool _lightFlinchOverlayActive;
        RecoveryGetUpType _pendingRecoveryGetUpType = RecoveryGetUpType.Back;
        CharacterPoseSnapshot _pendingRecoveryPoseSnapshot;
        bool _hasPendingRecoveryPoseSnapshot;
        bool _proceduralRecoilPending;
        bool _recoilSnapshotInitialized;
        HitContext _pendingHitContext;
        bool _recoveryPoseMatchingActive;
        float _recoveryPoseMatchElapsed;
        float _recoveryPoseMatchDuration;
        Transform[] _recoveryPoseBones;
        Quaternion[] _recoveryPoseFromRotations;
        Quaternion[] _recoveryPoseToRotations;
        bool _recoveryPlaybackActive;
        int _recoveryPlaybackLayerIndex;
        float _recoveryTargetDuration;
        bool _heavyStaggerPresentationActive;
        float _heavyStaggerPresentationElapsed;
        bool _heavyStaggerUsingDedicatedState;

        public bool IsWeaponEquipped => _weaponEquip.IsWeaponEquipped;

        public bool IsEquipPlaybackInProgress => _weaponEquip.IsEquipPlaybackInProgress;

        public bool IsLightFlinchOverlayActive => _lightFlinchOverlayActive;

        public WeaponEquipPlaybackController WeaponEquip => _weaponEquip;

        public CharacterAnimationPresenter(
            Animator animator,
            CharacterControllerConfig controllerConfig,
            CharacterAnimationConfig animConfig,
            Transform spineTransform = null,
            Transform torsoFacingTransform = null,
            Transform neckRecoilTransform = null,
            Transform headRecoilTransform = null)
        {
            _animator = animator;
            _controllerConfig = controllerConfig;
            _animConfig = animConfig;
            _spineTransform = spineTransform;
            _torsoFacingTransform = torsoFacingTransform;
            _neckRecoilTransform = neckRecoilTransform;
            _headRecoilTransform = headRecoilTransform;
            _weaponEquip = new WeaponEquipPlaybackController(animator, animConfig);

            if (_animConfig != null)
            {
                _speedHash = Animator.StringToHash(_animConfig.speedParam);
                _hitBlendXHash = Animator.StringToHash(_animConfig.hitBlendXParam);
                _hitBlendZHash = Animator.StringToHash(_animConfig.hitBlendZParam);
                _flinchWeightHash = Animator.StringToHash(_animConfig.flinchWeightParam);
                _movingHash = Animator.StringToHash(_animConfig.movingParam);
                _getUpBackStateHash = Animator.StringToHash(_animConfig.getUpBackStateName);
                _getUpFrontStateHash = Animator.StringToHash(_animConfig.getUpFrontStateName);
                _heavyStaggerStateHash = Animator.StringToHash(_animConfig.heavyStaggerStateName);

                if (_animator != null && !string.IsNullOrEmpty(_animConfig.fullBodyLayerName))
                {
                    _fullBodyLayerIndex = _animator.GetLayerIndex(_animConfig.fullBodyLayerName);
                    if (_fullBodyLayerIndex < 0)
                    {
                        Debug.LogWarning(
                            $"[CharacterAnimationPresenter] FullBody layer not found: {_animConfig.fullBodyLayerName}. " +
                            "Recovery get-up will fallback to base layer.\n" +
                            $"[CharacterAnimationPresenter] Missing layer '{_animConfig.fullBodyLayerName}'.",
                            _animator);
                    }
                }

                if (_animator != null && !string.IsNullOrEmpty(_animConfig.flinchLayerName))
                {
                    _flinchLayerIndex = _animator.GetLayerIndex(_animConfig.flinchLayerName);
                    if (_flinchLayerIndex < 0)
                    {
                        Debug.LogWarning(
                            $"[CharacterAnimationPresenter] Flinch layer not found: {_animConfig.flinchLayerName}. " +
                            "Run menu Active Ragdoll > Setup Player Flinch Layer (C2).\n" +
                            $"[CharacterAnimationPresenter] Missing layer '{_animConfig.flinchLayerName}'.",
                            _animator);
                    }
                }
            }

            ConfigureAnimatorForGameplay();
        }

        public void ConfigureAnimatorForGameplay()
        {
            if (_animator == null)
                return;

            _animator.applyRootMotion = false;
            _animator.updateMode = AnimatorUpdateMode.Normal;
        }

        bool CanWriteAnimator() =>
            _animator != null &&
            _animator.runtimeAnimatorController != null &&
            _animator.isInitialized;

        public void SyncLocomotionAnimator(float normalizedSpeed)
        {
            if (_animConfig == null || !CanWriteAnimator())
                return;

            _weaponEquip.SetLastNormalizedSpeed(normalizedSpeed);
            _animator.SetFloat(_speedHash, normalizedSpeed);
            _animator.SetBool(_movingHash, _weaponEquip.ResolveLocomotionMovingBool(normalizedSpeed));
        }

        public void SetLocomotionSpeed(float normalizedSpeed) => SyncLocomotionAnimator(normalizedSpeed);

        public bool TryToggleWeaponEquip() =>
            _weaponEquip.TryBeginToggle(
                _weaponEquip.LastNormalizedSpeed >= (_animConfig != null ? _animConfig.movingSpeedThreshold : 0.1f));

        public bool TryToggleWeaponEquip(bool movingAtPress) => _weaponEquip.TryBeginToggle(movingAtPress);

        public void SetWeaponEquippedState(bool equipped) => _weaponEquip.SetWeaponEquippedState(equipped);

        public void CompleteWeaponEquipPlayback() => _weaponEquip.CompleteWeaponEquipPlayback();

        public void SetEquippedAnimator(bool equipped) => _weaponEquip.SetEquippedAnimator(equipped);

        public void AbortWeaponEquipPlayback() => _weaponEquip.Abort();

        public void SetPendingRecoveryType(RecoveryGetUpType getUpType) =>
            _pendingRecoveryGetUpType = getUpType;

        public void SetPendingRecoveryPoseSnapshot(in CharacterPoseSnapshot snapshot)
        {
            _pendingRecoveryPoseSnapshot = snapshot;
            _hasPendingRecoveryPoseSnapshot = snapshot.IsValid;
        }

        /// <summary>
        /// 地面轻击：Flinch 动画 + 延后至 LateUpdate 的程序化弯曲
        /// Grounded light hit — flinch presentation; procedural recoil deferred to LateUpdate
        /// </summary>
        public void BeginLightFlinchOverlay(in HitContext hitContext)
        {
            _lightFlinchOverlayActive = true;
            _pendingHitContext = hitContext;
            _recoilSnapshotInitialized = false;
            BeginProceduralRecoilPending();

            if (!CanWriteAnimator() || _animConfig == null || _animator == null)
                return;

            var blend = HitDirectionUtility.ToAnimatorBlendLocal(in hitContext, _animator.transform);
            ApplyFlinchPresentation(1f, blend.x, blend.y);
        }

        /// <summary>
        /// 按 overlay 已持续时间更新 FlinchWeight 淡出
        /// Fade flinch weight by elapsed overlay time
        /// </summary>
        public void UpdateLightFlinchOverlay(float overlayElapsed)
        {
            if (!_lightFlinchOverlayActive || _controllerConfig == null)
                return;

            if (_animator == null || !_animator.enabled || !CanWriteAnimator() || _animConfig == null)
                return;

            var duration = Mathf.Max(_controllerConfig.lightFlinchDuration, 0.01f);
            var weight = Mathf.Clamp01(1f - overlayElapsed / duration);
            _animator.SetFloat(_flinchWeightHash, weight);

            if (_flinchLayerIndex >= 0)
                _animator.SetLayerWeight(_flinchLayerIndex, weight);
        }

        /// <summary>
        /// Animator 更新后写入脊柱/颈/头弯曲（由 Root LateUpdate 调用）
        /// Apply procedural recoil after animator — call from root LateUpdate
        /// </summary>
        public void ApplySpineRecoilLateUpdate(float overlayElapsed)
        {
            if (!_lightFlinchOverlayActive || _controllerConfig == null || !_proceduralRecoilPending)
                return;

            var facing = ResolveTorsoFacingTransform();
            if (facing == null)
                return;

            if (!_recoilSnapshotInitialized)
                InitializeRecoilSnapshot(in _pendingHitContext, facing);

            if (!_recoilSnapshotInitialized)
                return;

            var duration = Mathf.Max(_controllerConfig.lightFlinchDuration, 0.01f);
            var normalizedTime = Mathf.Clamp01(overlayElapsed / duration);
            var strength = EvaluateRecoilStrength(normalizedTime);

            ApplyBoneRecoil(_spineRecoil, strength);
            ApplyBoneRecoil(_neckRecoil, strength);
            ApplyBoneRecoil(_headRecoil, strength);
        }

        /// <summary>
        /// 结束轻击 overlay
        /// End light flinch overlay
        /// </summary>
        public void EndLightFlinchOverlay()
        {
            _lightFlinchOverlayActive = false;
            EndProceduralRecoil();

            if (CanWriteAnimator())
                ApplyFlinchPresentation(0f, 0f, 0f);
        }

        Transform ResolveTorsoFacingTransform() =>
            _torsoFacingTransform != null ? _torsoFacingTransform : _spineTransform;

        void BeginProceduralRecoilPending()
        {
            _proceduralRecoilPending = false;
            ResetBoneRecoilSlot(_spineRecoil);
            ResetBoneRecoilSlot(_neckRecoil);
            ResetBoneRecoilSlot(_headRecoil);

            if (_controllerConfig == null)
                return;

            if (QueueBoneRecoil(_spineRecoil, _spineTransform, _controllerConfig.lightFlinchSpineRecoilEnabled))
                _proceduralRecoilPending = true;
            if (QueueBoneRecoil(_neckRecoil, _neckRecoilTransform, _controllerConfig.lightFlinchNeckRecoilEnabled))
                _proceduralRecoilPending = true;
            if (QueueBoneRecoil(_headRecoil, _headRecoilTransform, _controllerConfig.lightFlinchHeadRecoilEnabled))
                _proceduralRecoilPending = true;
        }

        static bool QueueBoneRecoil(BoneRecoilSlot slot, Transform bone, bool enabled)
        {
            slot.Bone = bone;
            slot.Pending = enabled && bone != null;
            return slot.Pending;
        }

        static void ResetBoneRecoilSlot(BoneRecoilSlot slot)
        {
            slot.Pending = false;
            slot.Active = false;
            slot.HasLastApplied = false;
            slot.RecoilOffsetRotation = Quaternion.identity;
            slot.LastAppliedOffset = Quaternion.identity;
        }

        void InitializeRecoilSnapshot(in HitContext hitContext, Transform facingBone)
        {
            var any = false;

            if (_spineRecoil.Pending && _spineRecoil.Bone != null)
            {
                _spineRecoil.RecoilOffsetRotation = HitDirectionUtility.ComputeRecoilLocalOffset(
                    facingBone,
                    _spineRecoil.Bone,
                    in hitContext,
                    _controllerConfig.lightFlinchSpinePitchDegrees,
                    _controllerConfig.lightFlinchSpineRollDegrees);
                SnapshotBoneBase(_spineRecoil);
                any = true;
            }

            if (_neckRecoil.Pending && _neckRecoil.Bone != null)
            {
                _neckRecoil.RecoilOffsetRotation = HitDirectionUtility.ComputeRecoilLocalOffset(
                    facingBone,
                    _neckRecoil.Bone,
                    in hitContext,
                    _controllerConfig.lightFlinchNeckPitchDegrees,
                    _controllerConfig.lightFlinchNeckRollDegrees);
                SnapshotBoneBase(_neckRecoil);
                any = true;
            }

            if (_headRecoil.Pending && _headRecoil.Bone != null)
            {
                _headRecoil.RecoilOffsetRotation = HitDirectionUtility.ComputeRecoilLocalOffset(
                    facingBone,
                    _headRecoil.Bone,
                    in hitContext,
                    _controllerConfig.lightFlinchHeadPitchDegrees,
                    _controllerConfig.lightFlinchHeadRollDegrees);
                SnapshotBoneBase(_headRecoil);
                any = true;
            }

            _recoilSnapshotInitialized = any;
            _proceduralRecoilPending = any;
        }

        static void SnapshotBoneBase(BoneRecoilSlot slot)
        {
            slot.Active = true;
            slot.Pending = false;
            slot.HasLastApplied = false;
        }

        static void ApplyBoneRecoil(BoneRecoilSlot slot, float strength)
        {
            if (!slot.Active || slot.Bone == null)
                return;

            // Animator(Normal) 每帧会重写骨骼姿态；这里直接在当前动画姿态上叠加即可
            // Animator (Normal) rewrites pose every frame; just layer recoil on top of current pose
            var animBase = slot.Bone.localRotation;
            var applied = Quaternion.Slerp(Quaternion.identity, slot.RecoilOffsetRotation, strength);
            slot.Bone.localRotation = animBase * applied;
            slot.LastAppliedOffset = applied;
            slot.HasLastApplied = true;
        }

        void EndProceduralRecoil()
        {
            RestoreBoneRecoil(_spineRecoil);
            RestoreBoneRecoil(_neckRecoil);
            RestoreBoneRecoil(_headRecoil);
            _proceduralRecoilPending = false;
            _recoilSnapshotInitialized = false;
        }

        static void RestoreBoneRecoil(BoneRecoilSlot slot)
        {
            if (!slot.Active || slot.Bone == null)
                return;

            // 结束时不做反向抵消，避免与 Animator 当帧写入互相打架导致闪烁
            // Do not inverse-cancel at end; prevents one-frame snap against animator pose

            slot.Active = false;
            slot.HasLastApplied = false;
            slot.RecoilOffsetRotation = Quaternion.identity;
            slot.LastAppliedOffset = Quaternion.identity;
        }

        float EvaluateRecoilStrength(float normalizedOverlayTime)
        {
            var curve = _controllerConfig.lightFlinchSpineRecoilCurve;
            if (curve != null && curve.length > 0)
                return Mathf.Clamp01(curve.Evaluate(normalizedOverlayTime));

            return Mathf.Clamp01(1f - normalizedOverlayTime);
        }

        public void OnEnterState(CharacterState state, in HitContext hitContext)
        {
            if (!CanWriteAnimator() || _animConfig == null)
                return;

            switch (state)
            {
                case CharacterState.Locomotion:
                case CharacterState.WeaponEquipPlayback:
                    _animator.enabled = true;
                    EndHeavyStaggerPresentation();
                    if (!_lightFlinchOverlayActive)
                        ApplyFlinchPresentation(0f, 0f, 0f);
                    break;

                case CharacterState.LightFlinch:
                    _animator.enabled = true;
                    EndHeavyStaggerPresentation();
                    var blend = _animator != null
                        ? HitDirectionUtility.ToAnimatorBlendLocal(in hitContext, _animator.transform)
                        : Vector2.up;
                    ApplyFlinchPresentation(1f, blend.x, blend.y);
                    break;

                case CharacterState.HeavyStagger:
                    _animator.enabled = true;
                    EndLightFlinchOverlay();
                    BeginHeavyStaggerPresentation(in hitContext);
                    break;

                case CharacterState.Knockdown:
                case CharacterState.ForcedKnockdown:
                    _animator.enabled = false;
                    EndLightFlinchOverlay();
                    EndHeavyStaggerPresentation();
                    break;

                case CharacterState.Recovering:
                    _animator.enabled = true;
                    EndHeavyStaggerPresentation();
                    if (!_lightFlinchOverlayActive)
                        ApplyFlinchPresentation(0f, 0f, 0f);
                    BeginRecoveryPlayback(_pendingRecoveryGetUpType);
                    break;
            }
        }

        void BeginHeavyStaggerPresentation(in HitContext hitContext)
        {
            _heavyStaggerPresentationActive = true;
            _heavyStaggerPresentationElapsed = 0f;
            _heavyStaggerUsingDedicatedState = false;

            if (!CanWriteAnimator() || _animConfig == null)
                return;

            if (TryPlayHeavyStaggerDedicatedState(in hitContext))
            {
                _heavyStaggerUsingDedicatedState = true;
                return;
            }

            var blend = _animator != null
                ? HitDirectionUtility.ToAnimatorBlendLocal(in hitContext, _animator.transform)
                : Vector2.up;
            ApplyFlinchPresentation(1f, blend.x, blend.y);
        }

        void TickHeavyStaggerPresentation(float deltaTime)
        {
            if (!_heavyStaggerPresentationActive || _controllerConfig == null || !CanWriteAnimator() || _animConfig == null)
                return;
            if (_heavyStaggerUsingDedicatedState)
                return;

            _heavyStaggerPresentationElapsed += Mathf.Max(0f, deltaTime);
            var duration = Mathf.Max(0.01f, _controllerConfig.heavyBlendBackDuration);
            var weight = Mathf.Clamp01(1f - _heavyStaggerPresentationElapsed / duration);
            _animator.SetFloat(_flinchWeightHash, weight);
            if (_flinchLayerIndex >= 0)
                _animator.SetLayerWeight(_flinchLayerIndex, weight);
        }

        void EndHeavyStaggerPresentation()
        {
            if (!_heavyStaggerPresentationActive)
                return;

            _heavyStaggerPresentationActive = false;
            _heavyStaggerPresentationElapsed = 0f;
            _heavyStaggerUsingDedicatedState = false;

            if (CanWriteAnimator() && !_lightFlinchOverlayActive)
                ApplyFlinchPresentation(0f, 0f, 0f);
        }

        bool TryPlayHeavyStaggerDedicatedState(in HitContext hitContext)
        {
            if (!CanWriteAnimator() || _animConfig == null || _animator == null || _heavyStaggerStateHash == 0)
                return false;

            // 重击专用动画替代 flinch 淡出，先清空武器 overlay 并重置 flinch 权重
            // Dedicated heavy animation replaces flinch fade; clear weapon overlay and flinch weight first
            _weaponEquip.Abort();

            var blend = HitDirectionUtility.ToAnimatorBlendLocal(in hitContext, _animator.transform);
            ApplyFlinchPresentation(0f, blend.x, blend.y);

            var layerIndex = ResolveHeavyStaggerPlaybackLayerIndex(_heavyStaggerStateHash);
            if (layerIndex < 0)
                return false;

            var duration = Mathf.Max(0.01f, _animConfig.heavyStaggerCrossFadeDuration);
            _animator.CrossFadeInFixedTime(_heavyStaggerStateHash, duration, layerIndex);
            return true;
        }

        int ResolveHeavyStaggerPlaybackLayerIndex(int stateHash)
        {
            var hasFull = _fullBodyLayerIndex >= 0 && _animator != null && _animator.HasState(_fullBodyLayerIndex, stateHash);
            if (hasFull)
            {
                _animator.SetLayerWeight(_fullBodyLayerIndex, 1f);
                return _fullBodyLayerIndex;
            }

            var hasBase = _animator != null && _animator.HasState(0, stateHash);
            return hasBase ? 0 : -1;
        }

        void BeginRecoveryPlayback(RecoveryGetUpType getUpType)
        {
            if (!CanWriteAnimator() || _animConfig == null)
                return;

            CancelRecoveryPlaybackSpeedControl();

            // 进入 Recovering 前强制清零武器 overlay，避免 FullBody/UpBody 残留权重影响起身
            // Force-clear weapon overlay weights before recovery get-up playback
            _weaponEquip.Abort();

            var stateHash = getUpType == RecoveryGetUpType.Back
                ? _getUpBackStateHash
                : _getUpFrontStateHash;

            if (stateHash == 0)
                return;

            var layerIndex = ResolveRecoveryPlaybackLayerIndex(stateHash);
            if (layerIndex < 0)
            {
                Debug.LogWarning(
                    "[CharacterAnimationPresenter] 起身状态未在可用 Layer 中找到，请检查状态名和 Layer 配置。\n" +
                    "[CharacterAnimationPresenter] Get-up state not found on available layers; check state names/layer config.",
                    _animator);
                return;
            }

            if (TryBeginRecoveryPoseMatch(stateHash, layerIndex))
                return;

            var duration = Mathf.Max(0.01f, _animConfig.getUpCrossFadeDuration);
            _animator.CrossFadeInFixedTime(stateHash, duration, layerIndex);
            BeginRecoverySpeedControl(getUpType, stateHash, layerIndex);
        }

        int ResolveRecoveryPlaybackLayerIndex(int stateHash)
        {
            if (_animConfig == null)
                return _animator != null && _animator.HasState(0, stateHash) ? 0 : -1;

            var hasBase = _animator != null && _animator.HasState(0, stateHash);
            var hasFull = _fullBodyLayerIndex >= 0 && _animator != null && _animator.HasState(_fullBodyLayerIndex, stateHash);

            // 优先遵循配置，但若目标层不存在该状态则自动回退到可用层
            // Prefer configured layer, fallback to any layer that has the state
            if (_animConfig.recoveryUseFullBodyLayer)
            {
                if (hasFull)
                {
                    _animator.SetLayerWeight(_fullBodyLayerIndex, 1f);
                    return _fullBodyLayerIndex;
                }
                if (hasBase)
                    return 0;
            }
            else
            {
                if (hasBase)
                    return 0;
                if (hasFull)
                {
                    _animator.SetLayerWeight(_fullBodyLayerIndex, 1f);
                    return _fullBodyLayerIndex;
                }
            }

            return -1;
        }

        bool TryBeginRecoveryPoseMatch(int stateHash, int layerIndex)
        {
            if (!_hasPendingRecoveryPoseSnapshot || !_pendingRecoveryPoseSnapshot.IsValid)
                return false;
            if (_animConfig.recoveryPoseMatchDuration <= 0.0001f)
                return false;

            _animator.speed = 0f;
            _animator.Play(stateHash, layerIndex, 0f);
            _animator.Update(0f);

            var sourceBones = _pendingRecoveryPoseSnapshot.Bones;
            var sourceRotations = _pendingRecoveryPoseSnapshot.LocalRotations;
            var count = sourceBones.Length;
            var maxClamp = Mathf.Max(0f, _animConfig.recoveryMaxBoneAngleClamp);
            var validCount = 0;

            var bones = new Transform[count];
            var fromRotations = new Quaternion[count];
            var toRotations = new Quaternion[count];

            for (var i = 0; i < count; i++)
            {
                var bone = sourceBones[i];
                if (bone == null)
                    continue;

                var from = sourceRotations[i];
                var to = bone.localRotation;

                if (maxClamp > 0.001f)
                {
                    var angle = Quaternion.Angle(from, to);
                    if (angle > maxClamp)
                        to = Quaternion.Slerp(from, to, maxClamp / angle);
                }

                bones[validCount] = bone;
                fromRotations[validCount] = from;
                toRotations[validCount] = to;
                validCount++;
            }

            if (validCount == 0)
            {
                _animator.speed = 1f;
                _hasPendingRecoveryPoseSnapshot = false;
                return false;
            }

            if (validCount != count)
            {
                System.Array.Resize(ref bones, validCount);
                System.Array.Resize(ref fromRotations, validCount);
                System.Array.Resize(ref toRotations, validCount);
            }

            _recoveryPoseBones = bones;
            _recoveryPoseFromRotations = fromRotations;
            _recoveryPoseToRotations = toRotations;
            _recoveryPoseMatchElapsed = 0f;
            _recoveryPoseMatchDuration = Mathf.Max(0.01f, _animConfig.recoveryPoseMatchDuration);
            _recoveryPoseMatchingActive = true;
            _hasPendingRecoveryPoseSnapshot = false;
            BeginRecoverySpeedControl(_pendingRecoveryGetUpType, stateHash, layerIndex);
            return true;
        }

        public void TickRecoveryPoseMatchLateUpdate(float deltaTime)
        {
            if (!_recoveryPoseMatchingActive || _recoveryPoseBones == null || _recoveryPoseBones.Length == 0)
                return;

            _recoveryPoseMatchElapsed += Mathf.Max(0f, deltaTime);
            var normalized = Mathf.Clamp01(_recoveryPoseMatchElapsed / _recoveryPoseMatchDuration);
            var weight = EvaluateRecoveryPoseMatchWeight(normalized);

            for (var i = 0; i < _recoveryPoseBones.Length; i++)
            {
                var bone = _recoveryPoseBones[i];
                if (bone == null)
                    continue;
                bone.localRotation = Quaternion.Slerp(_recoveryPoseFromRotations[i], _recoveryPoseToRotations[i], weight);
            }

            if (normalized >= 1f)
                EndRecoveryPoseMatch();
        }

        float EvaluateRecoveryPoseMatchWeight(float normalizedTime)
        {
            var curve = _animConfig != null ? _animConfig.recoveryPoseMatchCurve : null;
            if (curve != null && curve.length > 0)
                return Mathf.Clamp01(curve.Evaluate(normalizedTime));
            return Mathf.SmoothStep(0f, 1f, normalizedTime);
        }

        void EndRecoveryPoseMatch()
        {
            if (_recoveryPoseBones != null)
            {
                for (var i = 0; i < _recoveryPoseBones.Length; i++)
                {
                    var bone = _recoveryPoseBones[i];
                    if (bone == null)
                        continue;
                    bone.localRotation = _recoveryPoseToRotations[i];
                }
            }

            _animator.speed = 1f;
            _recoveryPoseMatchingActive = false;
            _recoveryPoseMatchElapsed = 0f;
            _recoveryPoseMatchDuration = 0f;
            _recoveryPoseBones = null;
            _recoveryPoseFromRotations = null;
            _recoveryPoseToRotations = null;
        }

        void CancelRecoveryPoseMatch()
        {
            _recoveryPoseMatchingActive = false;
            _recoveryPoseMatchElapsed = 0f;
            _recoveryPoseMatchDuration = 0f;
            _recoveryPoseBones = null;
            _recoveryPoseFromRotations = null;
            _recoveryPoseToRotations = null;
            _hasPendingRecoveryPoseSnapshot = false;

            if (_animator != null)
                _animator.speed = 1f;
        }

        void BeginRecoverySpeedControl(RecoveryGetUpType getUpType, int stateHash, int layerIndex)
        {
            _recoveryPlaybackActive = true;
            _recoveryPlaybackLayerIndex = layerIndex;
            _recoveryTargetDuration = ResolveRecoveryTargetDuration(getUpType);
        }

        float ResolveRecoveryTargetDuration(RecoveryGetUpType getUpType)
        {
            if (_animConfig == null)
                return 1f;

            var configured = getUpType == RecoveryGetUpType.Back
                ? _animConfig.getUpBackTargetDuration
                : _animConfig.getUpFrontTargetDuration;
            return Mathf.Max(0.1f, configured);
        }

        void TickRecoveryPlaybackSpeedControl()
        {
            if (!_recoveryPlaybackActive || _animator == null)
                return;
            if (_recoveryPoseMatchingActive)
                return;
            if (_recoveryPlaybackLayerIndex < 0)
                return;
            if (_animator.IsInTransition(_recoveryPlaybackLayerIndex))
                return;

            var clipLength = ResolveRecoveryClipLengthSeconds(_recoveryPlaybackLayerIndex);
            if (clipLength <= 0.0001f)
                return;
            var targetDuration = Mathf.Max(0.1f, _recoveryTargetDuration);
            var speed = Mathf.Clamp(clipLength / targetDuration, 0.01f, 5f);
            _animator.speed = speed;
        }

        float ResolveRecoveryClipLengthSeconds(int layerIndex)
        {
            if (_animator == null || layerIndex < 0)
                return 0f;

            var clips = _animator.GetCurrentAnimatorClipInfo(layerIndex);
            if (clips != null && clips.Length > 0 && clips[0].clip != null)
                return Mathf.Max(0.01f, clips[0].clip.length);

            var info = _animator.GetCurrentAnimatorStateInfo(layerIndex);
            return Mathf.Max(0.01f, info.length);
        }

        void CancelRecoveryPlaybackSpeedControl()
        {
            _recoveryPlaybackActive = false;
            _recoveryPlaybackLayerIndex = -1;
            _recoveryTargetDuration = 0f;

            if (_animator != null)
                _animator.speed = 1f;
        }

        public void OnExitState(CharacterState state)
        {
            if (state == CharacterState.Recovering)
            {
                CancelRecoveryPoseMatch();
                CancelRecoveryPlaybackSpeedControl();
            }
            if (state == CharacterState.HeavyStagger)
                EndHeavyStaggerPresentation();

            if (!CanWriteAnimator() || _animConfig == null)
                return;

            if (state == CharacterState.LightFlinch && !_lightFlinchOverlayActive)
                ApplyFlinchPresentation(0f, 0f, 0f);
        }

        public void OnTickState(CharacterState state, float deltaTime)
        {
            if (state == CharacterState.WeaponEquipPlayback)
                _weaponEquip.Tick(deltaTime);
            else if (state == CharacterState.Recovering)
                TickRecoveryPlaybackSpeedControl();
            else if (state == CharacterState.HeavyStagger)
                TickHeavyStaggerPresentation(deltaTime);
        }

        public void OnFixedTickState(CharacterState state, float fixedDeltaTime) { }

        void ApplyFlinchPresentation(float weight, float hitBlendX, float hitBlendZ)
        {
            _animator.SetFloat(_flinchWeightHash, weight);
            _animator.SetFloat(_hitBlendXHash, hitBlendX);
            _animator.SetFloat(_hitBlendZHash, hitBlendZ);

            if (_flinchLayerIndex >= 0)
                _animator.SetLayerWeight(_flinchLayerIndex, weight);
        }
    }
}
