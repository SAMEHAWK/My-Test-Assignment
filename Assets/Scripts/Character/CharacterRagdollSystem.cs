using System;
using System.Collections.Generic;
using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// Ragdoll 运行模式
    /// Ragdoll runtime mode
    /// </summary>
    public enum CharacterRagdollMode
    {
        Animated,
        PartialRagdoll,
        FullRagdoll,
        PoseMatching,
        Recovering
    }

    /// <summary>
    /// 角色 Ragdoll 外观：Root 只依赖此边界，内部实现后续可替换为双骨架
    /// Character ragdoll facade — Root depends on this boundary; internals can later become dual-skeleton
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterRagdollSystem : MonoBehaviour, ICharacterModule
    {
        [Header("Dual Skeleton / 双骨架")]
        [Tooltip("启用双骨架实现；未满足条件时自动回退旧单骨架 — Enable dual-skeleton path; falls back to legacy when unavailable")]
        [SerializeField] bool enableDualSkeleton = true;
        [Tooltip("允许回退旧单骨架实现（过渡期）— Allow legacy single-skeleton fallback during transition")]
        [SerializeField] bool allowLegacyFallback = true;
        [Tooltip("可见动画骨架根（通常是 VisualModel 或其 Armature）— Visual animated skeleton root")]
        [SerializeField] Transform visualRoot;
        [Tooltip("隐藏物理骨架根（通常是 RagdollRig）— Hidden physics skeleton root")]
        [SerializeField] Transform physicsRoot;
        [Tooltip("可见骨架 Animator（用于自动定位 Visual Root）— Visual animator for auto-resolve")]
        [SerializeField] Animator visualAnimator;
        [Tooltip("重击局部链目录（可选）；有配置时优先按链选骨 — Optional heavy-reaction chain catalog; preferred over fallback selection")]
        [SerializeField] RagdollChainCatalog chainCatalog;
        [Tooltip("Ragdoll 系统参数；分配后优先使用该配置 — Ragdoll system config; takes priority when assigned")]
        [SerializeField] RagdollSystemConfig systemConfig;
        [Tooltip("重击局部物理时长覆盖（<0 使用配置）— Override heavy partial hold duration (<0 uses config)")]
        [SerializeField] float heavyPartialHoldDurationOverride = -1f;
        [Tooltip("重击局部回融时长覆盖（<0 使用配置）— Override heavy blend-back duration (<0 uses config)")]
        [SerializeField] float heavyPartialBlendBackDurationOverride = -1f;
        [Tooltip("击倒沉降速度阈值覆盖（<0 使用配置）— Override settle speed threshold (<0 uses config)")]
        [SerializeField] float settleSpeedThresholdOverride = -1f;

        struct DualBoneBinding
        {
            public Transform VisualBone;
            public Transform PhysicsBone;
            public Rigidbody Body;
            public Quaternion CapturedPhysicsLocalRotation;
        }

        struct PartialBodyRuntimeState
        {
            public RigidbodyInterpolation Interpolation;
            public CollisionDetectionMode CollisionDetectionMode;
            public int SolverIterations;
            public int SolverVelocityIterations;
        }

        RagdollModule _legacyModule;
        Transform _fallbackCameraAnchor;
        readonly List<DualBoneBinding> _dualBindings = new();
        readonly Dictionary<Rigidbody, int> _dualBindingIndexByBody = new();
        readonly List<int> _partialBindingIndices = new();
        readonly Dictionary<int, float> _partialImpulseWeightByIndex = new();
        readonly Dictionary<int, float> _partialWritebackWeightByIndex = new();
        readonly Dictionary<int, PartialBodyRuntimeState> _partialRuntimeStateByIndex = new();

        bool _useDualSkeleton;
        CharacterRagdollMode _mode = CharacterRagdollMode.Animated;
        float _knockdownTimer;

        float _minKnockdownDuration;
        float _placeholderSettleDuration;
        float _settleSpeedThreshold;
        float _settleStableDuration;
        float _dualSettleStableTimer;
        float _heavyPartialRadius;
        int _heavyPartialMaxBodies;
        float _heavyPartialPhysicsHoldDuration;
        float _heavyPartialPrimaryImpulseMultiplier;
        float _heavyPartialSecondaryImpulseMultiplier;
        float _heavyPartialAngularImpulseMultiplier;
        int _heavyPartialSolverIterations;
        int _heavyPartialSolverVelocityIterations;
        float _heavyPartialBlendBackDuration;
        AnimationCurve _heavyPartialBlendBackCurve;

        bool _heavyPartialActive;
        bool _heavyPartialPhysicsActive;
        bool _heavyPartialBlendActive;
        float _heavyPartialPhysicsElapsed;
        float _heavyPartialBlendElapsed;
        float _heavyPartialBlendWeight;
        float _debugLastHitTime = -1f;
        Vector3 _debugLastImpulsePoint;
        Vector3 _debugLastImpulseDirection = Vector3.forward;
        Transform _debugPrimaryAffectedBone;
        readonly List<Transform> _debugAffectedBones = new();
        bool _legacyFallbackWarningLogged;

        public bool IsInitialized => _useDualSkeleton || _legacyModule != null;
        public bool IsLegacyFallbackAllowed => allowLegacyFallback;
        public bool IsUsingDualSkeleton => _useDualSkeleton;
        public bool IsUsingLegacyFallback => !_useDualSkeleton && _legacyModule != null;
        public string BackendStatus
        {
            get
            {
                if (_useDualSkeleton)
                    return "Dual";
                if (_legacyModule != null)
                    return "LegacyFallback";
                return "Unavailable";
            }
        }
        public CharacterRagdollMode Mode => _mode;
        public int MappedBoneCount => _useDualSkeleton ? _dualBindings.Count : 0;
        public string ActiveChainName { get; private set; } = "None";
        public bool HasDebugHitInfo => _debugLastHitTime >= 0f;
        public float DebugLastHitTime => _debugLastHitTime;
        public Vector3 DebugImpulsePoint => _debugLastImpulsePoint;
        public Vector3 DebugImpulseDirection => _debugLastImpulseDirection;
        public Transform DebugPrimaryAffectedBone => _debugPrimaryAffectedBone;
        public IReadOnlyList<Transform> DebugAffectedBones => _debugAffectedBones;
        public bool IsSettled => _useDualSkeleton ? IsDualSettled() : _legacyModule == null || _legacyModule.IsSettled;
        public bool IsHeavyPartialBlendComplete => _useDualSkeleton ? !_heavyPartialActive : _legacyModule == null || _legacyModule.IsHeavyPartialBlendComplete;
        public Transform CameraFollowAnchor => ResolveCameraFollowAnchor();

        /// <summary>
        /// 运行时初始化：优先双骨架，失败时按开关回退旧单骨架
        /// Runtime init: prefer dual-skeleton; fallback to legacy single-skeleton when enabled
        /// </summary>
        public void InitializeRuntime(
            Transform ownerTransform,
            Rigidbody[] ragdollBodies,
            CharacterControllerConfig controllerConfig,
            CharacterAnimationConfig animationConfig,
            float minKnockdownDuration,
            float placeholderSettleDuration,
            float settleSpeedThreshold,
            Transform fallbackCameraAnchor)
        {
            _fallbackCameraAnchor = fallbackCameraAnchor != null ? fallbackCameraAnchor : ownerTransform;

            var resolvedMinKnockdownDuration = systemConfig != null ? systemConfig.minKnockdownDuration : minKnockdownDuration;
            var resolvedPlaceholderSettleDuration = systemConfig != null ? systemConfig.placeholderSettleDuration : placeholderSettleDuration;
            var resolvedSettleSpeedThreshold = systemConfig != null ? systemConfig.settleSpeedThreshold : settleSpeedThreshold;
            var resolvedSettleStableDuration = systemConfig != null ? systemConfig.settleStableDuration : 0.15f;
            var resolvedHeavyPartialRadius = systemConfig != null
                ? systemConfig.heavyPartialRadius
                : controllerConfig != null ? controllerConfig.heavyPartialRagdollRadius : 0.65f;
            var resolvedHeavyPartialMaxBodies = systemConfig != null
                ? systemConfig.heavyPartialMaxBodies
                : controllerConfig != null ? controllerConfig.heavyPartialRagdollMaxBodies : 6;
            var resolvedHeavyPartialPhysicsHoldDuration = systemConfig != null
                ? systemConfig.heavyPartialPhysicsHoldDuration
                : controllerConfig != null ? controllerConfig.heavyPartialPhysicsHoldDuration : 0.18f;
            var resolvedHeavyPartialPrimaryImpulseMultiplier = systemConfig != null
                ? systemConfig.heavyPartialPrimaryImpulseMultiplier
                : controllerConfig != null ? controllerConfig.heavyPartialPrimaryImpulseMultiplier : 0.85f;
            var resolvedHeavyPartialSecondaryImpulseMultiplier = systemConfig != null
                ? systemConfig.heavyPartialSecondaryImpulseMultiplier
                : controllerConfig != null ? controllerConfig.heavyPartialSecondaryImpulseMultiplier : 0.45f;
            var resolvedHeavyPartialAngularImpulseMultiplier = systemConfig != null
                ? systemConfig.heavyPartialAngularImpulseMultiplier
                : controllerConfig != null ? controllerConfig.heavyPartialAngularImpulseMultiplier : 0.18f;
            var resolvedHeavyPartialSolverIterations = systemConfig != null
                ? systemConfig.heavyPartialSolverIterations
                : controllerConfig != null ? controllerConfig.heavyPartialSolverIterations : 12;
            var resolvedHeavyPartialSolverVelocityIterations = systemConfig != null
                ? systemConfig.heavyPartialSolverVelocityIterations
                : controllerConfig != null ? controllerConfig.heavyPartialSolverVelocityIterations : 4;
            var resolvedHeavyPartialBlendBackDuration = systemConfig != null
                ? systemConfig.heavyPartialBlendBackDuration
                : animationConfig != null ? animationConfig.heavyPartialBlendBackDuration : 0.75f;
            var resolvedHeavyPartialBlendBackCurve = systemConfig != null
                ? systemConfig.heavyPartialBlendBackCurve
                : animationConfig != null ? animationConfig.heavyPartialBlendBackCurve : null;

            _minKnockdownDuration = Mathf.Max(0.6f, resolvedMinKnockdownDuration);
            _placeholderSettleDuration = Mathf.Max(0.1f, resolvedPlaceholderSettleDuration);
            _settleSpeedThreshold = settleSpeedThresholdOverride >= 0f
                ? settleSpeedThresholdOverride
                : Mathf.Max(0.01f, resolvedSettleSpeedThreshold);
            _settleStableDuration = Mathf.Max(0f, resolvedSettleStableDuration);
            _heavyPartialRadius = Mathf.Max(0.2f, resolvedHeavyPartialRadius);
            _heavyPartialMaxBodies = Mathf.Max(1, resolvedHeavyPartialMaxBodies);
            _heavyPartialPhysicsHoldDuration = heavyPartialHoldDurationOverride >= 0f
                ? Mathf.Max(0.02f, heavyPartialHoldDurationOverride)
                : Mathf.Max(0.02f, resolvedHeavyPartialPhysicsHoldDuration);
            _heavyPartialPrimaryImpulseMultiplier = Mathf.Max(0f, resolvedHeavyPartialPrimaryImpulseMultiplier);
            _heavyPartialSecondaryImpulseMultiplier = Mathf.Max(0f, resolvedHeavyPartialSecondaryImpulseMultiplier);
            _heavyPartialAngularImpulseMultiplier = Mathf.Max(0f, resolvedHeavyPartialAngularImpulseMultiplier);
            _heavyPartialSolverIterations = Mathf.Max(1, resolvedHeavyPartialSolverIterations);
            _heavyPartialSolverVelocityIterations = Mathf.Max(1, resolvedHeavyPartialSolverVelocityIterations);
            _heavyPartialBlendBackDuration = heavyPartialBlendBackDurationOverride >= 0f
                ? Mathf.Max(0.01f, heavyPartialBlendBackDurationOverride)
                : Mathf.Max(0.01f, resolvedHeavyPartialBlendBackDuration);
            _heavyPartialBlendBackCurve = resolvedHeavyPartialBlendBackCurve;

            _useDualSkeleton = TryInitializeDualSkeleton(ownerTransform);
            if (_useDualSkeleton)
            {
                _legacyModule = null;
                return;
            }

            if (!allowLegacyFallback)
            {
                _legacyModule = null;
#if UNITY_EDITOR
                Debug.LogWarning(
                    "[CharacterRagdollSystem] 双骨架初始化失败且已禁用 legacy 回退，Ragdoll 后端不可用。\n" +
                    "[CharacterRagdollSystem] Dual init failed and legacy fallback is disabled; ragdoll backend unavailable.",
                    this);
#endif
                return;
            }

            _legacyModule = new RagdollModule(
                ownerTransform,
                ragdollBodies,
                _minKnockdownDuration,
                _placeholderSettleDuration,
                _settleSpeedThreshold,
                _heavyPartialBlendBackDuration,
                _heavyPartialBlendBackCurve,
                _heavyPartialRadius,
                _heavyPartialMaxBodies,
                _heavyPartialPhysicsHoldDuration,
                _heavyPartialPrimaryImpulseMultiplier,
                _heavyPartialSecondaryImpulseMultiplier,
                _heavyPartialAngularImpulseMultiplier,
                _heavyPartialSolverIterations,
                _heavyPartialSolverVelocityIterations);

            if (!_legacyFallbackWarningLogged)
            {
#if UNITY_EDITOR
                Debug.LogWarning(
                    "[CharacterRagdollSystem] 正在使用 legacy 单骨架回退路径（仅过渡期）。建议修复双骨架映射并关闭 Allow Legacy Fallback。\n" +
                    "[CharacterRagdollSystem] Using legacy single-skeleton fallback (transition only). Fix dual setup and disable Allow Legacy Fallback.",
                    this);
#endif
                _legacyFallbackWarningLogged = true;
            }
        }

        public void OnEnterState(CharacterState state, in HitContext hitContext)
        {
            if (!_useDualSkeleton)
            {
                _legacyModule?.OnEnterState(state, in hitContext);
                return;
            }

            switch (state)
            {
                case CharacterState.HeavyStagger:
                    PlayHeavyReaction(in hitContext);
                    break;
                case CharacterState.Knockdown:
                case CharacterState.ForcedKnockdown:
                    EnterFullRagdoll(in hitContext);
                    break;
                case CharacterState.Recovering:
                    ReturnToAnimated();
                    _mode = CharacterRagdollMode.Recovering;
                    break;
                default:
                    ReturnToAnimated();
                    break;
            }
        }

        public void OnExitState(CharacterState state)
        {
            if (!_useDualSkeleton)
            {
                _legacyModule?.OnExitState(state);
                return;
            }

            if (state is CharacterState.HeavyStagger or CharacterState.Knockdown or CharacterState.ForcedKnockdown)
                ReturnToAnimated();
        }

        public void OnTickState(CharacterState state, float deltaTime)
        {
            if (!_useDualSkeleton)
            {
                _legacyModule?.OnTickState(state, deltaTime);
                return;
            }

            if (state is CharacterState.Knockdown or CharacterState.ForcedKnockdown)
            {
                _knockdownTimer += Mathf.Max(0f, deltaTime);
                WritebackFullRagdollPoseToVisual();
                UpdateDualSettleTimer(deltaTime);
                return;
            }

            if (state == CharacterState.HeavyStagger && _heavyPartialActive)
            {
                if (_heavyPartialPhysicsActive)
                    TickHeavyPartialPhysicsHold(deltaTime);
                else if (_heavyPartialBlendActive)
                    TickHeavyPartialBlendBack(deltaTime);
            }
        }

        public void OnFixedTickState(CharacterState state, float fixedDeltaTime)
        {
            if (!_useDualSkeleton)
                _legacyModule?.OnFixedTickState(state, fixedDeltaTime);
        }

        public void ApplyHeavyPartialPoseLateUpdate()
        {
            if (!_useDualSkeleton)
            {
                _legacyModule?.ApplyHeavyPartialPoseLateUpdate();
                return;
            }

            if (!_heavyPartialActive || _heavyPartialPhysicsActive || _partialBindingIndices.Count == 0)
                return;

            for (var i = 0; i < _partialBindingIndices.Count; i++)
            {
                var bindingIndex = _partialBindingIndices[i];
                if (bindingIndex < 0 || bindingIndex >= _dualBindings.Count)
                    continue;

                var binding = _dualBindings[bindingIndex];
                if (binding.VisualBone == null)
                    continue;

                var animLocalRotation = binding.VisualBone.localRotation;
                var writebackWeight = ResolvePartialWritebackWeight(bindingIndex);
                var weightedPhysicsRotation = Quaternion.Slerp(animLocalRotation, binding.CapturedPhysicsLocalRotation, writebackWeight);
                binding.VisualBone.localRotation = _heavyPartialBlendActive
                    ? Quaternion.Slerp(weightedPhysicsRotation, animLocalRotation, _heavyPartialBlendWeight)
                    : weightedPhysicsRotation;
            }
        }

        /// <summary>
        /// Root LateUpdate 调用：同步双骨架模式下的姿态
        /// Called by root LateUpdate to sync dual-skeleton poses
        /// </summary>
        public void LateTick(float deltaTime)
        {
            if (!_useDualSkeleton)
                return;

            if (_mode is CharacterRagdollMode.Animated or CharacterRagdollMode.Recovering)
                SyncKinematicPhysicsToVisual();
        }

        public CharacterPoseSnapshot CaptureRecoveryPose() =>
            _useDualSkeleton ? CaptureDualRecoveryPose() : _legacyModule != null ? _legacyModule.CaptureRecoveryPoseSnapshot() : default;

        public RagdollAnchor CaptureRecoveryAnchor() =>
            _useDualSkeleton ? CaptureDualRecoveryAnchor() : _legacyModule != null ? _legacyModule.CaptureRecoveryAnchor() : default;

        /// <summary>
        /// 评估起身类型：优先根据双骨架胸口朝向判定仰/俯；旧路径回退为 Back
        /// Evaluate get-up type from dual-skeleton torso facing; legacy path falls back to Back
        /// </summary>
        public RecoveryGetUpType EvaluateGetUpType() =>
            _useDualSkeleton ? EvaluateDualGetUpType() : RecoveryGetUpType.Back;

        /// <summary>
        /// 预检查是否具备双骨架初始化条件（不创建运行时映射）
        /// Precheck whether dual-skeleton setup is available without building runtime bindings
        /// </summary>
        public bool CanAttemptDualSkeletonSetup(Transform ownerTransform)
        {
            if (!enableDualSkeleton)
                return false;

            var resolvedAnimator = ResolveVisualAnimator();
            var resolvedVisualRoot = ResolveVisualRoot(resolvedAnimator);
            var resolvedPhysicsRoot = ResolvePhysicsRoot(ownerTransform);
            if (resolvedVisualRoot == null || resolvedPhysicsRoot == null)
                return false;

            // 仅要求 Physics Root 下至少有刚体，进一步的映射有效性在初始化阶段判断
            // Require at least one rigidbody under Physics Root; full mapping validity is checked in initialization
            return resolvedPhysicsRoot.GetComponentsInChildren<Rigidbody>(true).Length > 0;
        }

        bool TryInitializeDualSkeleton(Transform ownerTransform)
        {
            if (!enableDualSkeleton)
                return false;

            visualAnimator = ResolveVisualAnimator();
            visualRoot = ResolveVisualRoot(visualAnimator);
            physicsRoot = ResolvePhysicsRoot(ownerTransform);

            if (visualRoot == null || physicsRoot == null)
                return false;

            BuildDualBoneBindings();
            if (_dualBindings.Count == 0)
                return false;

            DisableHeavyPartialImmediate();
            ReturnAllBodiesToKinematic();
            SyncKinematicPhysicsToVisual();

            _mode = CharacterRagdollMode.Animated;
            _knockdownTimer = 0f;
            _dualSettleStableTimer = 0f;
            return true;
        }

        Animator ResolveVisualAnimator() =>
            visualAnimator != null ? visualAnimator : GetComponentInChildren<Animator>();

        Transform ResolveVisualRoot(Animator resolvedAnimator) =>
            visualRoot != null ? visualRoot : resolvedAnimator != null ? resolvedAnimator.transform : null;

        Transform ResolvePhysicsRoot(Transform ownerTransform) =>
            physicsRoot != null ? physicsRoot : FindChildByName(ownerTransform != null ? ownerTransform : transform, "RagdollRig");

        void BuildDualBoneBindings()
        {
            _dualBindings.Clear();
            _dualBindingIndexByBody.Clear();

            var visualByName = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            foreach (var visualBone in visualRoot.GetComponentsInChildren<Transform>(true))
            {
                if (visualBone == null)
                    continue;
                if (!visualByName.ContainsKey(visualBone.name))
                    visualByName.Add(visualBone.name, visualBone);
            }

            foreach (var body in physicsRoot.GetComponentsInChildren<Rigidbody>(true))
            {
                if (body == null)
                    continue;
                if (!visualByName.TryGetValue(body.transform.name, out var visualBone))
                    continue;

                var index = _dualBindings.Count;
                _dualBindings.Add(new DualBoneBinding
                {
                    VisualBone = visualBone,
                    PhysicsBone = body.transform,
                    Body = body,
                    CapturedPhysicsLocalRotation = body.transform.localRotation
                });
                _dualBindingIndexByBody[body] = index;
            }
        }

        static Transform FindChildByName(Transform root, string exactName)
        {
            if (root == null || string.IsNullOrEmpty(exactName))
                return null;

            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child != null && string.Equals(child.name, exactName, StringComparison.OrdinalIgnoreCase))
                    return child;
            }

            return null;
        }

        void PlayHeavyReaction(in HitContext hitContext)
        {
            DisableHeavyPartialImmediate();

            SelectHeavyPartialBindings(hitContext.ContactPoint, _heavyPartialRadius, _heavyPartialMaxBodies, _partialBindingIndices);
            if (_partialBindingIndices.Count == 0)
            {
                ActiveChainName = "None";
                return;
            }

            var worldDir = HitDirectionUtility.ResolveIncomingWorld(in hitContext, transform).normalized;
            var impulse = ResolveImpulse(in hitContext);
            var force = worldDir * impulse;
            var primaryBindingIndex = _partialBindingIndices[0];
            var maxRadius = Mathf.Max(0.01f, _heavyPartialRadius);

            for (var i = 0; i < _partialBindingIndices.Count; i++)
            {
                var bindingIndex = _partialBindingIndices[i];
                if (bindingIndex < 0 || bindingIndex >= _dualBindings.Count)
                    continue;

                var binding = _dualBindings[bindingIndex];
                if (binding.Body == null)
                    continue;

                PreparePartialBodyRuntime(bindingIndex, binding.Body);
                SetDynamic(binding.Body);
                binding.Body.WakeUp();
                var propagationWeight = ResolvePartialImpulseWeight(bindingIndex);

                if (bindingIndex == primaryBindingIndex)
                {
                    binding.Body.AddForceAtPosition(
                        force * _heavyPartialPrimaryImpulseMultiplier * propagationWeight,
                        hitContext.ContactPoint,
                        ForceMode.Impulse);

                    var armVector = binding.Body.worldCenterOfMass - hitContext.ContactPoint;
                    var torqueAxis = Vector3.Cross(armVector, force);
                    if (torqueAxis.sqrMagnitude > 0.0001f)
                        binding.Body.AddTorque(
                            torqueAxis.normalized * impulse * _heavyPartialAngularImpulseMultiplier * propagationWeight,
                            ForceMode.Impulse);
                }
                else
                {
                    var distance = Vector3.Distance(binding.Body.worldCenterOfMass, hitContext.ContactPoint);
                    var t = Mathf.Clamp01(distance / maxRadius);
                    var attenuatedScale = Mathf.Lerp(1f, 0.35f, t) * _heavyPartialSecondaryImpulseMultiplier;
                    binding.Body.AddForce(force * attenuatedScale * propagationWeight, ForceMode.Impulse);
                }
            }

            _heavyPartialActive = true;
            _heavyPartialPhysicsActive = true;
            _heavyPartialBlendActive = false;
            _heavyPartialPhysicsElapsed = 0f;
            _heavyPartialBlendElapsed = 0f;
            _heavyPartialBlendWeight = 0f;
            _mode = CharacterRagdollMode.PartialRagdoll;
            RecordDebugHit(
                hitContext.ContactPoint,
                worldDir,
                primaryBindingIndex,
                _partialBindingIndices);
        }

        void EnterFullRagdoll(in HitContext hitContext)
        {
            DisableHeavyPartialImmediate();
            _mode = CharacterRagdollMode.FullRagdoll;
            _knockdownTimer = 0f;
            _dualSettleStableTimer = 0f;

            var worldDir = HitDirectionUtility.ResolveIncomingWorld(in hitContext, transform).normalized;
            var impulse = ResolveImpulse(in hitContext);
            var force = worldDir * impulse;
            var sorted = BuildBindingIndicesSortedByDistance(hitContext.ContactPoint);
            var primaryBindingIndex = sorted.Count > 0 ? sorted[0] : -1;
            var fullBodyIndices = new List<int>(_dualBindings.Count);

            for (var i = 0; i < _dualBindings.Count; i++)
            {
                var body = _dualBindings[i].Body;
                if (body == null)
                    continue;

                SetDynamic(body);
                body.WakeUp();
                body.AddForceAtPosition(force, hitContext.ContactPoint, ForceMode.Impulse);
                fullBodyIndices.Add(i);
            }

            RecordDebugHit(
                hitContext.ContactPoint,
                worldDir,
                primaryBindingIndex,
                fullBodyIndices);
        }

        void ReturnToAnimated()
        {
            DisableHeavyPartialImmediate();
            ReturnAllBodiesToKinematic();
            _knockdownTimer = 0f;
            _dualSettleStableTimer = 0f;
            if (_mode == CharacterRagdollMode.FullRagdoll || _mode == CharacterRagdollMode.PoseMatching)
                _mode = CharacterRagdollMode.Animated;
            else if (_mode != CharacterRagdollMode.Recovering)
                _mode = CharacterRagdollMode.Animated;
        }

        void ReturnAllBodiesToKinematic()
        {
            for (var i = 0; i < _dualBindings.Count; i++)
            {
                var body = _dualBindings[i].Body;
                if (body == null)
                    continue;
                SetKinematic(body);
            }
        }

        void SyncKinematicPhysicsToVisual()
        {
            for (var i = 0; i < _dualBindings.Count; i++)
            {
                var binding = _dualBindings[i];
                if (binding.VisualBone == null || binding.PhysicsBone == null || binding.Body == null)
                    continue;

                if (!binding.Body.isKinematic)
                    continue;

                binding.PhysicsBone.SetPositionAndRotation(binding.VisualBone.position, binding.VisualBone.rotation);
            }
        }

        void WritebackFullRagdollPoseToVisual()
        {
            if (_mode != CharacterRagdollMode.FullRagdoll)
                return;

            for (var i = 0; i < _dualBindings.Count; i++)
            {
                var binding = _dualBindings[i];
                if (binding.VisualBone == null || binding.PhysicsBone == null)
                    continue;
                binding.VisualBone.localPosition = binding.PhysicsBone.localPosition;
                binding.VisualBone.localRotation = binding.PhysicsBone.localRotation;
            }
        }

        void SelectHeavyPartialBindings(Vector3 contactPoint, float radius, int maxBodies, List<int> outputIndices)
        {
            outputIndices.Clear();
            _partialImpulseWeightByIndex.Clear();
            _partialWritebackWeightByIndex.Clear();
            if (_dualBindings.Count == 0)
                return;

            var sorted = BuildBindingIndicesSortedByDistance(contactPoint);
            if (sorted.Count == 0)
                return;

            var primaryIndex = sorted[0];
            AddBindingIndexUnique(outputIndices, primaryIndex);

            var primaryBinding = _dualBindings[primaryIndex];
            if (primaryBinding.PhysicsBone == null)
                return;

            if (TrySelectByChainCatalog(primaryBinding, contactPoint, radius, maxBodies, outputIndices))
                return;

            var radiusSqr = Mathf.Max(0.01f, radius * radius);
            var candidates = new List<(int index, float sqrDistance)>();
            for (var i = 0; i < _dualBindings.Count; i++)
            {
                if (outputIndices.Count >= maxBodies)
                    break;

                var binding = _dualBindings[i];
                if (binding.Body == null || binding.PhysicsBone == null)
                    continue;
                if (!binding.PhysicsBone.IsChildOf(primaryBinding.PhysicsBone))
                    continue;

                var sqrDistance = (binding.Body.worldCenterOfMass - contactPoint).sqrMagnitude;
                if (sqrDistance > radiusSqr)
                    continue;

                candidates.Add((i, sqrDistance));
            }

            candidates.Sort((a, b) => a.sqrDistance.CompareTo(b.sqrDistance));
            for (var i = 0; i < candidates.Count && outputIndices.Count < maxBodies; i++)
                AddBindingIndexUnique(outputIndices, candidates[i].index);

            for (var i = 0; i < outputIndices.Count; i++)
            {
                var index = outputIndices[i];
                _partialImpulseWeightByIndex[index] = 1f;
                _partialWritebackWeightByIndex[index] = 1f;
            }

            ActiveChainName = "AutoSubtree";
        }

        bool TrySelectByChainCatalog(
            DualBoneBinding primaryBinding,
            Vector3 contactPoint,
            float radius,
            int maxBodies,
            List<int> outputIndices)
        {
            if (chainCatalog == null || primaryBinding.PhysicsBone == null)
                return false;

            if (!chainCatalog.TryResolveByBoneName(primaryBinding.PhysicsBone.name, out var chain) || chain == null)
                return false;

            var primaryIndex = outputIndices.Count > 0 ? outputIndices[0] : -1;
            if (primaryIndex < 0)
                return false;

            for (var i = 0; i < _dualBindings.Count; i++)
            {
                if (outputIndices.Count >= maxBodies)
                    break;

                var binding = _dualBindings[i];
                if (binding.Body == null || binding.PhysicsBone == null)
                    continue;

                if (!TryEvaluateChainWeight(primaryBinding.PhysicsBone, binding.PhysicsBone, chain, out var hierarchyWeight))
                    continue;

                var distanceWeight = EvaluateDistanceWeight(contactPoint, radius, binding.Body.worldCenterOfMass);
                var finalWeight = Mathf.Clamp01(hierarchyWeight * distanceWeight);
                if (finalWeight < Mathf.Clamp01(chain.minPropagationWeight))
                    continue;

                AddBindingIndexUnique(outputIndices, i);
                _partialImpulseWeightByIndex[i] = finalWeight * Mathf.Max(0f, chain.impulseMultiplier);
                _partialWritebackWeightByIndex[i] = Mathf.Clamp01(finalWeight * Mathf.Clamp01(chain.writebackWeight));
            }

            if (outputIndices.Count == 0)
                return false;

            // 保证主骨始终保持最高优先级，避免传播覆盖主受击表现
            // Keep primary bone dominant so propagated weights do not suppress main reaction
            _partialImpulseWeightByIndex[primaryIndex] = Mathf.Max(
                _partialImpulseWeightByIndex.TryGetValue(primaryIndex, out var primaryImpulseWeight) ? primaryImpulseWeight : 0f,
                Mathf.Max(1f, chain.impulseMultiplier));
            _partialWritebackWeightByIndex[primaryIndex] = Mathf.Max(
                _partialWritebackWeightByIndex.TryGetValue(primaryIndex, out var primaryWritebackWeight) ? primaryWritebackWeight : 0f,
                Mathf.Clamp01(chain.writebackWeight));

            ActiveChainName = string.IsNullOrWhiteSpace(chain.chainName) ? "ConfiguredChain" : chain.chainName;
            return true;
        }

        bool TryEvaluateChainWeight(
            Transform primaryBone,
            Transform candidateBone,
            RagdollChainDefinition chain,
            out float hierarchyWeight)
        {
            hierarchyWeight = 0f;
            if (primaryBone == null || candidateBone == null || chain == null)
                return false;

            if (candidateBone == primaryBone)
            {
                hierarchyWeight = 1f;
                return true;
            }

            var parentDepth = GetAncestorDepth(primaryBone, candidateBone);
            var childDepth = GetAncestorDepth(candidateBone, primaryBone);

            switch (chain.propagationMode)
            {
                case RagdollChainPropagationMode.SelfOnly:
                    return false;

                case RagdollChainPropagationMode.Children:
                    if (!chain.includeChildren || childDepth <= 0 || childDepth > Mathf.Max(0, chain.maxChildDepth))
                        return false;
                    hierarchyWeight = Mathf.Pow(Mathf.Clamp01(chain.childFalloff), childDepth);
                    return true;

                case RagdollChainPropagationMode.ParentAndChildren:
                    if (parentDepth > 0)
                    {
                        if (parentDepth > Mathf.Max(0, chain.maxParentDepth))
                            return false;
                        hierarchyWeight = Mathf.Pow(Mathf.Clamp01(chain.parentFalloff), parentDepth);
                        return true;
                    }

                    if (!chain.includeChildren || childDepth <= 0 || childDepth > Mathf.Max(0, chain.maxChildDepth))
                        return false;
                    hierarchyWeight = Mathf.Pow(Mathf.Clamp01(chain.childFalloff), childDepth);
                    return true;
            }

            return false;
        }

        static int GetAncestorDepth(Transform node, Transform ancestor)
        {
            if (node == null || ancestor == null)
                return -1;

            var depth = 0;
            var cursor = node.parent;
            while (cursor != null)
            {
                depth++;
                if (cursor == ancestor)
                    return depth;
                cursor = cursor.parent;
            }

            return -1;
        }

        static float EvaluateDistanceWeight(Vector3 contactPoint, float radius, Vector3 centerOfMass)
        {
            var maxRadius = Mathf.Max(0.01f, radius);
            var distance = Vector3.Distance(centerOfMass, contactPoint);
            var normalized = Mathf.Clamp01(distance / maxRadius);
            return Mathf.Lerp(1f, 0.35f, normalized);
        }

        List<int> BuildBindingIndicesSortedByDistance(Vector3 contactPoint)
        {
            var temp = new List<(int index, float sqrDistance)>(_dualBindings.Count);
            for (var i = 0; i < _dualBindings.Count; i++)
            {
                var body = _dualBindings[i].Body;
                if (body == null)
                    continue;
                var sqrDistance = (body.worldCenterOfMass - contactPoint).sqrMagnitude;
                temp.Add((i, sqrDistance));
            }

            temp.Sort((a, b) => a.sqrDistance.CompareTo(b.sqrDistance));
            var sortedIndices = new List<int>(temp.Count);
            for (var i = 0; i < temp.Count; i++)
                sortedIndices.Add(temp[i].index);
            return sortedIndices;
        }

        static void AddBindingIndexUnique(List<int> output, int bindingIndex)
        {
            if (bindingIndex < 0 || output.Contains(bindingIndex))
                return;
            output.Add(bindingIndex);
        }

        float ResolvePartialImpulseWeight(int bindingIndex)
        {
            return _partialImpulseWeightByIndex.TryGetValue(bindingIndex, out var weight)
                ? Mathf.Max(0f, weight)
                : 1f;
        }

        float ResolvePartialWritebackWeight(int bindingIndex)
        {
            return _partialWritebackWeightByIndex.TryGetValue(bindingIndex, out var weight)
                ? Mathf.Clamp01(weight)
                : 1f;
        }

        void RecordDebugHit(
            Vector3 contactPoint,
            Vector3 impulseDirection,
            int primaryBindingIndex,
            List<int> affectedBindingIndices)
        {
            _debugLastHitTime = Time.time;
            _debugLastImpulsePoint = contactPoint;

            var direction = impulseDirection;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
                direction = transform.forward;
            _debugLastImpulseDirection = direction.normalized;

            if (primaryBindingIndex >= 0 && primaryBindingIndex < _dualBindings.Count)
                _debugPrimaryAffectedBone = _dualBindings[primaryBindingIndex].PhysicsBone;
            else
                _debugPrimaryAffectedBone = null;

            _debugAffectedBones.Clear();
            if (affectedBindingIndices == null)
                return;

            for (var i = 0; i < affectedBindingIndices.Count; i++)
            {
                var bindingIndex = affectedBindingIndices[i];
                if (bindingIndex < 0 || bindingIndex >= _dualBindings.Count)
                    continue;
                var bone = _dualBindings[bindingIndex].PhysicsBone;
                if (bone == null || _debugAffectedBones.Contains(bone))
                    continue;
                _debugAffectedBones.Add(bone);
            }
        }

        void PreparePartialBodyRuntime(int bindingIndex, Rigidbody body)
        {
            if (!_partialRuntimeStateByIndex.ContainsKey(bindingIndex))
            {
                _partialRuntimeStateByIndex[bindingIndex] = new PartialBodyRuntimeState
                {
                    Interpolation = body.interpolation,
                    CollisionDetectionMode = body.collisionDetectionMode,
                    SolverIterations = body.solverIterations,
                    SolverVelocityIterations = body.solverVelocityIterations
                };
            }

            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.solverIterations = _heavyPartialSolverIterations;
            body.solverVelocityIterations = _heavyPartialSolverVelocityIterations;
        }

        void RestorePartialBodyRuntime(int bindingIndex, Rigidbody body)
        {
            if (body == null)
                return;

            if (_partialRuntimeStateByIndex.TryGetValue(bindingIndex, out var state))
            {
                body.interpolation = state.Interpolation;
                body.collisionDetectionMode = state.CollisionDetectionMode;
                body.solverIterations = state.SolverIterations;
                body.solverVelocityIterations = state.SolverVelocityIterations;
            }
        }

        void TickHeavyPartialPhysicsHold(float deltaTime)
        {
            _heavyPartialPhysicsElapsed += Mathf.Max(0f, deltaTime);
            if (_heavyPartialPhysicsElapsed < _heavyPartialPhysicsHoldDuration)
                return;

            BeginHeavyPartialBlendBack();
        }

        void BeginHeavyPartialBlendBack()
        {
            for (var i = 0; i < _partialBindingIndices.Count; i++)
            {
                var bindingIndex = _partialBindingIndices[i];
                if (bindingIndex < 0 || bindingIndex >= _dualBindings.Count)
                    continue;

                var binding = _dualBindings[bindingIndex];
                if (binding.Body == null || binding.PhysicsBone == null)
                    continue;

                binding.CapturedPhysicsLocalRotation = binding.PhysicsBone.localRotation;
                _dualBindings[bindingIndex] = binding;

                RestorePartialBodyRuntime(bindingIndex, binding.Body);
                SetKinematic(binding.Body);
            }

            _heavyPartialPhysicsActive = false;
            _heavyPartialBlendActive = true;
            _heavyPartialBlendElapsed = 0f;
            _heavyPartialBlendWeight = 0f;
            _mode = CharacterRagdollMode.PoseMatching;
        }

        void TickHeavyPartialBlendBack(float deltaTime)
        {
            _heavyPartialBlendElapsed += Mathf.Max(0f, deltaTime);
            var normalized = Mathf.Clamp01(_heavyPartialBlendElapsed / _heavyPartialBlendBackDuration);
            _heavyPartialBlendWeight = EvaluateHeavyPartialBlendWeight(normalized);

            if (normalized >= 1f || _heavyPartialBlendWeight >= 0.999f)
                CompleteHeavyPartialBlendBack();
        }

        float EvaluateHeavyPartialBlendWeight(float normalized)
        {
            if (_heavyPartialBlendBackCurve != null && _heavyPartialBlendBackCurve.length > 0)
                return Mathf.Clamp01(_heavyPartialBlendBackCurve.Evaluate(normalized));
            return Mathf.SmoothStep(0f, 1f, normalized);
        }

        void CompleteHeavyPartialBlendBack()
        {
            for (var i = 0; i < _partialBindingIndices.Count; i++)
            {
                var bindingIndex = _partialBindingIndices[i];
                if (bindingIndex < 0 || bindingIndex >= _dualBindings.Count)
                    continue;

                var body = _dualBindings[bindingIndex].Body;
                if (body == null)
                    continue;

                RestorePartialBodyRuntime(bindingIndex, body);
                SetKinematic(body);
            }

            _partialBindingIndices.Clear();
            _partialImpulseWeightByIndex.Clear();
            _partialWritebackWeightByIndex.Clear();
            _partialRuntimeStateByIndex.Clear();
            _heavyPartialActive = false;
            _heavyPartialBlendActive = false;
            _heavyPartialPhysicsActive = false;
            _heavyPartialBlendElapsed = 0f;
            _heavyPartialBlendWeight = 1f;
            _mode = CharacterRagdollMode.Animated;
        }

        void DisableHeavyPartialImmediate()
        {
            for (var i = 0; i < _partialBindingIndices.Count; i++)
            {
                var bindingIndex = _partialBindingIndices[i];
                if (bindingIndex < 0 || bindingIndex >= _dualBindings.Count)
                    continue;

                var body = _dualBindings[bindingIndex].Body;
                if (body == null)
                    continue;

                RestorePartialBodyRuntime(bindingIndex, body);
                SetKinematic(body);
            }

            _partialBindingIndices.Clear();
            _partialImpulseWeightByIndex.Clear();
            _partialWritebackWeightByIndex.Clear();
            _partialRuntimeStateByIndex.Clear();
            _heavyPartialActive = false;
            _heavyPartialBlendActive = false;
            _heavyPartialPhysicsActive = false;
            _heavyPartialBlendElapsed = 0f;
            _heavyPartialPhysicsElapsed = 0f;
            _heavyPartialBlendWeight = 0f;
            ActiveChainName = "None";
        }

        bool IsDualSettled()
        {
            if (_mode != CharacterRagdollMode.FullRagdoll)
                return true;

            if (_knockdownTimer < _minKnockdownDuration)
                return false;

            if (_dualBindings.Count == 0)
                return _knockdownTimer >= _placeholderSettleDuration;

            return _dualSettleStableTimer >= _settleStableDuration;
        }

        void UpdateDualSettleTimer(float deltaTime)
        {
            if (_mode != CharacterRagdollMode.FullRagdoll)
            {
                _dualSettleStableTimer = 0f;
                return;
            }

            if (AreAllDynamicBodiesBelowThreshold())
            {
                _dualSettleStableTimer += Mathf.Max(0f, deltaTime);
            }
            else
            {
                _dualSettleStableTimer = 0f;
            }
        }

        bool AreAllDynamicBodiesBelowThreshold()
        {
            for (var i = 0; i < _dualBindings.Count; i++)
            {
                var body = _dualBindings[i].Body;
                if (body == null || body.isKinematic)
                    continue;
                if (body.linearVelocity.magnitude > _settleSpeedThreshold)
                    return false;
            }

            return true;
        }

        CharacterPoseSnapshot CaptureDualRecoveryPose()
        {
            if (_dualBindings.Count == 0)
                return default;

            var bones = new Transform[_dualBindings.Count];
            var rotations = new Quaternion[_dualBindings.Count];
            var count = 0;

            for (var i = 0; i < _dualBindings.Count; i++)
            {
                var binding = _dualBindings[i];
                if (binding.VisualBone == null || binding.PhysicsBone == null)
                    continue;

                bones[count] = binding.VisualBone;
                rotations[count] = binding.PhysicsBone.localRotation;
                count++;
            }

            if (count == 0)
                return default;

            if (count != bones.Length)
            {
                Array.Resize(ref bones, count);
                Array.Resize(ref rotations, count);
            }

            return new CharacterPoseSnapshot(bones, rotations);
        }

        RagdollAnchor CaptureDualRecoveryAnchor()
        {
            var hips = FindBindingByNameContains("hip");
            if (hips.PhysicsBone == null)
                return new RagdollAnchor(Vector3.zero, transform.forward, false);

            var chest = FindBindingByNameContains("chest");
            if (chest.PhysicsBone == null)
                chest = FindBindingByNameContains("spine");

            var hipsPos = hips.PhysicsBone.position;
            var planarForward = transform.forward;
            if (chest.PhysicsBone != null)
            {
                var toChest = chest.PhysicsBone.position - hipsPos;
                toChest.y = 0f;
                if (toChest.sqrMagnitude > 0.0001f)
                    planarForward = toChest.normalized;
            }

            planarForward.y = 0f;
            if (planarForward.sqrMagnitude < 0.0001f)
                planarForward = transform.forward;

            return new RagdollAnchor(hipsPos, planarForward.normalized, true);
        }

        DualBoneBinding FindBindingByNameContains(string keyword)
        {
            for (var i = 0; i < _dualBindings.Count; i++)
            {
                var binding = _dualBindings[i];
                var physicsBone = binding.PhysicsBone;
                if (physicsBone == null)
                    continue;
                if (physicsBone.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return binding;
            }

            return default;
        }

        RecoveryGetUpType EvaluateDualGetUpType()
        {
            var torso = FindBindingByNameContains("chest");
            if (torso.PhysicsBone == null)
                torso = FindBindingByNameContains("spine");

            if (torso.PhysicsBone != null)
            {
                // 胸口 forward 朝上视为仰躺起身（Back），朝下视为俯卧起身（Front）
                // Torso forward pointing up means back get-up; down means front get-up
                var forwardDot = Vector3.Dot(torso.PhysicsBone.forward, Vector3.up);
                if (Mathf.Abs(forwardDot) > 0.05f)
                    return forwardDot > 0f ? RecoveryGetUpType.Back : RecoveryGetUpType.Front;

                var upDot = Vector3.Dot(torso.PhysicsBone.up, Vector3.up);
                return upDot >= 0f ? RecoveryGetUpType.Back : RecoveryGetUpType.Front;
            }

            return RecoveryGetUpType.Back;
        }

        Transform ResolveCameraFollowAnchor()
        {
            if (_useDualSkeleton)
            {
                if (_mode == CharacterRagdollMode.FullRagdoll)
                {
                    var hips = FindBindingByNameContains("hip");
                    if (hips.PhysicsBone != null)
                        return hips.PhysicsBone;
                }

                if (_mode == CharacterRagdollMode.PartialRagdoll && _partialBindingIndices.Count > 0)
                {
                    var bindingIndex = _partialBindingIndices[0];
                    if (bindingIndex >= 0 && bindingIndex < _dualBindings.Count)
                    {
                        var anchor = _dualBindings[bindingIndex].PhysicsBone;
                        if (anchor != null)
                            return anchor;
                    }
                }
            }

            return _fallbackCameraAnchor != null ? _fallbackCameraAnchor : transform;
        }

        float ResolveImpulse(in HitContext hitContext)
        {
            if (hitContext.Impulse >= 0f)
                return hitContext.Impulse;

            return hitContext.Type switch
            {
                HitType.Heavy => 6f,
                HitType.ForceKnockdownHeavy => 6f,
                HitType.ForceKnockdownLight => 2f,
                _ => 2f
            };
        }

        static void SetDynamic(Rigidbody body)
        {
            if (body == null)
                return;
            body.isKinematic = false;
        }

        static void SetKinematic(Rigidbody body)
        {
            if (body == null)
                return;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }
    }
}
