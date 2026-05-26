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
        [Tooltip("启用双骨架实现；未满足条件时后端不可用 — Enable dual-skeleton path; backend becomes unavailable when setup fails")]
        [SerializeField] bool enableDualSkeleton = true;
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
        [Tooltip("局部重击表面接触点探测距离（米）— Probe distance for resolving partial heavy-hit surface contact")]
        [SerializeField] float heavyPartialContactProbeDistance = 1.5f;

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
        static readonly string[] HeadAliases = { "head" };
        static readonly string[] ChestAliases = { "upperchest", "chest", "spine2", "spine1", "spine" };
        static readonly string[] HipsAliases = { "hips", "pelvis", "hip" };
        static readonly string[] LeftArmAliases = { "leftupperarm", "leftarm", "lupperarm", "larm", "leftforearm", "leftlowerarm", "lforearm", "llowerarm" };
        static readonly string[] RightArmAliases = { "rightupperarm", "rightarm", "rupperarm", "rarm", "rightforearm", "rightlowerarm", "rforearm", "rlowerarm" };
        static readonly string[] LeftElbowAliases = { "leftforearm", "leftlowerarm", "leftelbow", "lforearm", "llowerarm", "lelbow" };
        static readonly string[] RightElbowAliases = { "rightforearm", "rightlowerarm", "rightelbow", "rforearm", "rlowerarm", "relbow" };
        static readonly string[] LeftWristAliases = { "lefthand", "leftwrist", "lhand", "lwrist" };
        static readonly string[] RightWristAliases = { "righthand", "rightwrist", "rhand", "rwrist" };
        static readonly string[] LeftLegAliases = { "leftupleg", "leftthigh", "leftleg", "leftcalf", "lupleg", "lthigh", "lleg", "lcalf" };
        static readonly string[] RightLegAliases = { "rightupleg", "rightthigh", "rightleg", "rightcalf", "rupleg", "rthigh", "rleg", "rcalf" };
        static readonly string[] LeftKneeAliases = { "leftleg", "leftcalf", "leftknee", "lleg", "lcalf", "lknee" };
        static readonly string[] RightKneeAliases = { "rightleg", "rightcalf", "rightknee", "rleg", "rcalf", "rknee" };
        static readonly string[] LeftAnkleAliases = { "leftfoot", "leftankle", "lfoot", "lankle" };
        static readonly string[] RightAnkleAliases = { "rightfoot", "rightankle", "rfoot", "rankle" };
        public bool IsInitialized => _useDualSkeleton;
        public bool IsUsingDualSkeleton => _useDualSkeleton;
        public string BackendStatus
        {
            get
            {
                if (_useDualSkeleton)
                    return "Dual";
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
        public bool IsSettled => _useDualSkeleton ? IsDualSettled() : true;
        public bool IsHeavyPartialBlendComplete => _useDualSkeleton ? !_heavyPartialActive : true;
        public Transform CameraFollowAnchor => ResolveCameraFollowAnchor();

        /// <summary>
        /// 按调试命中部位解析物理骨架接触点，优先用于重击调试稳定选骨
        /// Resolve physics contact point by debug region for stable heavy-hit selection
        /// </summary>
        public bool TryGetDebugContactPoint(DebugHitContactRegion region, out Vector3 point, out string matchedPhysicsBoneName)
        {
            point = transform.position + Vector3.up;
            matchedPhysicsBoneName = string.Empty;
            if (!_useDualSkeleton || _dualBindings.Count == 0)
                return false;

            var aliases = GetContactRegionAliases(region);
            if (aliases == null || aliases.Length == 0)
                return false;

            var bestIndex = -1;
            var bestAliasRank = int.MaxValue;
            var bestDistanceSqr = float.MaxValue;
            var rootPosition = transform.position;
            for (var i = 0; i < _dualBindings.Count; i++)
            {
                var bone = _dualBindings[i].PhysicsBone;
                if (bone == null)
                    continue;

                var normalizedBoneName = NormalizeBoneToken(bone.name);
                if (string.IsNullOrEmpty(normalizedBoneName))
                    continue;

                var aliasRank = GetAliasRank(normalizedBoneName, aliases);
                if (aliasRank < 0)
                    continue;

                var distanceSqr = (bone.position - rootPosition).sqrMagnitude;
                var isBetterCandidate = aliasRank < bestAliasRank
                    || (aliasRank == bestAliasRank && distanceSqr < bestDistanceSqr);
                if (!isBetterCandidate)
                    continue;

                bestAliasRank = aliasRank;
                bestDistanceSqr = distanceSqr;
                bestIndex = i;
            }

            if (bestIndex < 0 || bestIndex >= _dualBindings.Count)
                return false;

            var matchedBone = _dualBindings[bestIndex].PhysicsBone;
            if (matchedBone == null)
                return false;

            point = matchedBone.position;
            matchedPhysicsBoneName = matchedBone.name;
            return true;
        }

        /// <summary>
        /// 运行时初始化：仅支持双骨架，初始化失败时后端保持 Unavailable
        /// Runtime init: dual-skeleton only; backend stays Unavailable when setup fails
        /// </summary>
        public void InitializeRuntime(
            Transform ownerTransform,
            Transform fallbackCameraAnchor)
        {
            const float defaultMinKnockdownDuration = 0.8f;
            const float defaultPlaceholderSettleDuration = 1.5f;
            const float defaultSettleSpeedThreshold = 0.15f;

            _fallbackCameraAnchor = fallbackCameraAnchor != null ? fallbackCameraAnchor : ownerTransform;
            if (systemConfig == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning(
                    "[CharacterRagdollSystem] 未分配 RagdollSystemConfig，正在使用内置默认值。建议在 Prefab 上显式绑定配置资产。\n" +
                    "[CharacterRagdollSystem] RagdollSystemConfig is not assigned; using built-in defaults. Assign config asset on prefab.",
                    this);
#endif
            }

            var resolvedMinKnockdownDuration = systemConfig != null ? systemConfig.minKnockdownDuration : defaultMinKnockdownDuration;
            var resolvedPlaceholderSettleDuration = systemConfig != null ? systemConfig.placeholderSettleDuration : defaultPlaceholderSettleDuration;
            var resolvedSettleSpeedThreshold = systemConfig != null ? systemConfig.settleSpeedThreshold : defaultSettleSpeedThreshold;
            var resolvedSettleStableDuration = systemConfig != null ? systemConfig.settleStableDuration : 0.15f;
            var resolvedHeavyPartialRadius = systemConfig != null
                ? systemConfig.heavyPartialRadius
                : 0.65f;
            var resolvedHeavyPartialMaxBodies = systemConfig != null
                ? systemConfig.heavyPartialMaxBodies
                : 6;
            var resolvedHeavyPartialPhysicsHoldDuration = systemConfig != null
                ? systemConfig.heavyPartialPhysicsHoldDuration
                : 0.18f;
            var resolvedHeavyPartialPrimaryImpulseMultiplier = systemConfig != null
                ? systemConfig.heavyPartialPrimaryImpulseMultiplier
                : 0.85f;
            var resolvedHeavyPartialSecondaryImpulseMultiplier = systemConfig != null
                ? systemConfig.heavyPartialSecondaryImpulseMultiplier
                : 0.45f;
            var resolvedHeavyPartialAngularImpulseMultiplier = systemConfig != null
                ? systemConfig.heavyPartialAngularImpulseMultiplier
                : 0.18f;
            var resolvedHeavyPartialSolverIterations = systemConfig != null
                ? systemConfig.heavyPartialSolverIterations
                : 12;
            var resolvedHeavyPartialSolverVelocityIterations = systemConfig != null
                ? systemConfig.heavyPartialSolverVelocityIterations
                : 4;
            var resolvedHeavyPartialBlendBackDuration = systemConfig != null
                ? systemConfig.heavyPartialBlendBackDuration
                : 0.75f;
            var resolvedHeavyPartialBlendBackCurve = systemConfig != null
                ? systemConfig.heavyPartialBlendBackCurve
                : null;

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
                return;

#if UNITY_EDITOR
            Debug.LogWarning(
                "[CharacterRagdollSystem] 双骨架初始化失败，Ragdoll 后端不可用。请检查 Visual Root / Physics Root / Animator / 映射骨骼。\n" +
                "[CharacterRagdollSystem] Dual-skeleton init failed; ragdoll backend unavailable. Check Visual Root / Physics Root / Animator / bone mapping.",
                this);
#endif
        }

        public void OnEnterState(CharacterState state, in HitContext hitContext)
        {
            if (!_useDualSkeleton)
                return;

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
                return;

            if (state is CharacterState.HeavyStagger or CharacterState.Knockdown or CharacterState.ForcedKnockdown)
                ReturnToAnimated();
        }

        public void OnTickState(CharacterState state, float deltaTime)
        {
            if (!_useDualSkeleton)
                return;

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
            _ = state;
            _ = fixedDeltaTime;
        }

        public void ApplyHeavyPartialPoseLateUpdate()
        {
            if (!_useDualSkeleton)
                return;

            if (!_heavyPartialActive || _partialBindingIndices.Count == 0)
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
                var physicsLocalRotation = _heavyPartialBlendActive
                    ? binding.CapturedPhysicsLocalRotation
                    : binding.PhysicsBone != null ? binding.PhysicsBone.localRotation : animLocalRotation;
                var writebackWeight = ResolvePartialWritebackWeight(bindingIndex);
                var weightedPhysicsRotation = Quaternion.Slerp(animLocalRotation, physicsLocalRotation, writebackWeight);
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
            _useDualSkeleton ? CaptureDualRecoveryPose() : default;

        public RagdollAnchor CaptureRecoveryAnchor() =>
            _useDualSkeleton ? CaptureDualRecoveryAnchor() : default;

        /// <summary>
        /// 评估起身类型：优先根据双骨架胸口朝向判定仰/俯；不可用时回退为 Back
        /// Evaluate get-up type from dual-skeleton torso facing; falls back to Back when unavailable
        /// </summary>
        public RecoveryGetUpType EvaluateGetUpType() =>
            _useDualSkeleton ? EvaluateDualGetUpType() : RecoveryGetUpType.Back;

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

            SyncKinematicPhysicsToVisual();
            SelectHeavyPartialBindings(hitContext.ContactPoint, _heavyPartialRadius, _heavyPartialMaxBodies, _partialBindingIndices);
            if (_partialBindingIndices.Count == 0)
            {
                ActiveChainName = "None";
                return;
            }

            BeginHeavyPartialFullPhysicsSimulation();

            var incoming = HitDirectionUtility.ResolveIncomingWorld(in hitContext, transform).normalized;
            var impulse = ResolveImpulse(in hitContext);
            // incoming 表示攻击源指向角色的施力方向，局部 Ragdoll 与调试箭头都使用同一方向
            // incoming is the force direction from attack source to character; partial ragdoll and debug arrow share it
            var forceDirection = incoming;
            var force = forceDirection * impulse;
            var primaryBindingIndex = _partialBindingIndices[0];
            var resolvedContactPoint = ResolveHeavyPartialContactPoint(
                primaryBindingIndex,
                hitContext.ContactPoint,
                forceDirection);
            var maxRadius = Mathf.Max(0.01f, _heavyPartialRadius);

            for (var i = 0; i < _partialBindingIndices.Count; i++)
            {
                var bindingIndex = _partialBindingIndices[i];
                if (bindingIndex < 0 || bindingIndex >= _dualBindings.Count)
                    continue;

                var binding = _dualBindings[bindingIndex];
                if (binding.Body == null)
                    continue;

                binding.Body.WakeUp();
                var propagationWeight = ResolvePartialImpulseWeight(bindingIndex);

                if (bindingIndex == primaryBindingIndex)
                {
                    binding.Body.AddForceAtPosition(
                        force * _heavyPartialPrimaryImpulseMultiplier * propagationWeight,
                        resolvedContactPoint,
                        ForceMode.Impulse);

                    var armVector = binding.Body.worldCenterOfMass - resolvedContactPoint;
                    var torqueAxis = Vector3.Cross(armVector, force);
                    if (torqueAxis.sqrMagnitude > 0.0001f)
                        binding.Body.AddTorque(
                            torqueAxis.normalized * impulse * _heavyPartialAngularImpulseMultiplier * propagationWeight,
                            ForceMode.Impulse);
                }
                else
                {
                    var distance = Vector3.Distance(binding.Body.worldCenterOfMass, resolvedContactPoint);
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
                resolvedContactPoint,
                forceDirection,
                primaryBindingIndex,
                _partialBindingIndices);
        }

        void EnterFullRagdoll(in HitContext hitContext)
        {
            DisableHeavyPartialImmediate();
            _mode = CharacterRagdollMode.FullRagdoll;
            _knockdownTimer = 0f;
            _dualSettleStableTimer = 0f;

            var incoming = HitDirectionUtility.ResolveIncomingWorld(in hitContext, transform).normalized;
            var impulse = ResolveImpulse(in hitContext);
            // incoming 表示攻击源指向角色的施力方向，全身 Ragdoll 与调试箭头都使用同一方向
            // incoming is the force direction from attack source to character; full ragdoll and debug arrow share it
            var forceDirection = incoming;
            var force = forceDirection * impulse;
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
                forceDirection,
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
                // 全身倒地回写使用世界空间，避免 Visual/Physics 根节点局部基准不一致导致方向错位
                // Use world-space writeback to avoid heading drift from mismatched local root bases
                binding.VisualBone.SetPositionAndRotation(
                    binding.PhysicsBone.position,
                    binding.PhysicsBone.rotation);
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

            if (TrySelectByChainCatalog(sorted, contactPoint, radius, maxBodies, outputIndices))
                return;

            var primaryIndex = sorted[0];
            AddBindingIndexUnique(outputIndices, primaryIndex);
            var primaryBinding = _dualBindings[primaryIndex];
            if (primaryBinding.PhysicsBone == null)
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
            List<int> sortedIndicesByDistance,
            Vector3 contactPoint,
            float radius,
            int maxBodies,
            List<int> outputIndices)
        {
            if (chainCatalog == null || sortedIndicesByDistance == null || sortedIndicesByDistance.Count == 0)
                return false;

            if (!TryResolveNearestCatalogPrimary(sortedIndicesByDistance, contactPoint, out var primaryIndex, out var chain))
                return false;

            if (primaryIndex < 0 || primaryIndex >= _dualBindings.Count)
                return false;

            var primaryBinding = _dualBindings[primaryIndex];
            if (primaryBinding.PhysicsBone == null || chain == null)
                return false;

            outputIndices.Clear();
            AddBindingIndexUnique(outputIndices, primaryIndex);

            var candidates = new List<(int index, float finalWeight, bool isParent, float distance)>(_dualBindings.Count);

            for (var i = 0; i < _dualBindings.Count; i++)
            {
                var binding = _dualBindings[i];
                if (binding.Body == null || binding.PhysicsBone == null)
                    continue;

                if (!TryEvaluateChainWeight(primaryBinding.PhysicsBone, binding.PhysicsBone, chain, out var hierarchyWeight))
                    continue;

                var distance = Vector3.Distance(binding.Body.worldCenterOfMass, contactPoint);
                var distanceWeight = EvaluateDistanceWeight(contactPoint, radius, binding.Body.worldCenterOfMass);
                var parentDepth = GetAncestorDepth(primaryBinding.PhysicsBone, binding.PhysicsBone);
                var isParent = parentDepth > 0;

                // 父链传播优先遵循链配置深度/衰减，不再叠加距离衰减，避免“配置允许父链但实际总被过滤”
                // Parent propagation follows chain depth/falloff directly, avoiding over-filter by distance
                var finalWeight = Mathf.Clamp01(isParent ? hierarchyWeight : hierarchyWeight * distanceWeight);
                if (finalWeight < Mathf.Clamp01(chain.minPropagationWeight))
                    continue;

                candidates.Add((i, finalWeight, isParent, distance));
            }

            if (candidates.Count == 0)
                return false;

            candidates.Sort((a, b) =>
            {
                // 同权重下优先父链，其次按距离近
                // Prefer parent candidates on tie, then nearer distance
                var weightCompare = b.finalWeight.CompareTo(a.finalWeight);
                if (weightCompare != 0)
                    return weightCompare;

                if (a.isParent != b.isParent)
                    return a.isParent ? -1 : 1;

                return a.distance.CompareTo(b.distance);
            });

            for (var i = 0; i < candidates.Count && outputIndices.Count < maxBodies; i++)
            {
                var candidate = candidates[i];
                AddBindingIndexUnique(outputIndices, candidate.index);
                _partialImpulseWeightByIndex[candidate.index] = candidate.finalWeight * Mathf.Max(0f, chain.impulseMultiplier);
                _partialWritebackWeightByIndex[candidate.index] = Mathf.Clamp01(candidate.finalWeight * Mathf.Clamp01(chain.writebackWeight));
            }

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

        bool TryResolveNearestCatalogPrimary(
            List<int> sortedIndicesByDistance,
            Vector3 contactPoint,
            out int primaryIndex,
            out RagdollChainDefinition chain)
        {
            primaryIndex = -1;
            chain = null;
            if (sortedIndicesByDistance == null || sortedIndicesByDistance.Count == 0 || chainCatalog == null)
                return false;

            const int maxCandidates = 16;
            const float hipsPrimaryPenalty = 0.22f;
            var bestScore = float.MaxValue;

            // 从近到远挑选候选，但不再“首命中即返回”。
            // 使用评分选择主骨，避免 hips 链在腿部命中时过度抢占。
            // Score nearest candidates instead of first-hit return to reduce hips hijacking on leg hits.
            for (var i = 0; i < sortedIndicesByDistance.Count && i < maxCandidates; i++)
            {
                var index = sortedIndicesByDistance[i];
                if (index < 0 || index >= _dualBindings.Count)
                    continue;

                var binding = _dualBindings[index];
                if (binding.PhysicsBone == null || binding.Body == null)
                    continue;

                if (!chainCatalog.TryResolveByBoneName(binding.PhysicsBone.name, out var resolved) || resolved == null)
                    continue;

                var score = Vector3.Distance(binding.PhysicsBone.position, contactPoint);
                if (IsHipsChain(resolved))
                    score += hipsPrimaryPenalty;

                if (score >= bestScore)
                    continue;

                bestScore = score;
                primaryIndex = index;
                chain = resolved;
            }

            return primaryIndex >= 0 && chain != null;
        }

        static bool IsHipsChain(RagdollChainDefinition chain)
        {
            if (chain == null)
                return false;

            var chainName = chain.chainName;
            if (!string.IsNullOrWhiteSpace(chainName) &&
                (chainName.IndexOf("hip", StringComparison.OrdinalIgnoreCase) >= 0
                 || chainName.IndexOf("pelvis", StringComparison.OrdinalIgnoreCase) >= 0))
                return true;

            var keywords = chain.boneNameKeywords;
            if (keywords == null)
                return false;

            for (var i = 0; i < keywords.Length; i++)
            {
                var keyword = keywords[i];
                if (string.IsNullOrWhiteSpace(keyword))
                    continue;
                if (keyword.IndexOf("hip", StringComparison.OrdinalIgnoreCase) >= 0
                    || keyword.IndexOf("pelvis", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
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

            var parentDepth = GetMappedAncestorDepth(primaryBone, candidateBone);
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

        int GetMappedAncestorDepth(Transform node, Transform ancestor)
        {
            if (node == null || ancestor == null)
                return -1;

            // 父级传导按已映射刚体计数，跳过肩/胸椎等无 Rigidbody 的中间骨，避免胸部被错误判定为过远。
            // Count mapped rigidbody ancestors only, skipping non-physical shoulder/spine links so chest propagation is not over-depth.
            var mappedDepth = 0;
            var cursor = node.parent;
            while (cursor != null)
            {
                if (HasMappedPhysicsBone(cursor))
                    mappedDepth++;

                if (cursor == ancestor)
                    return mappedDepth > 0 ? mappedDepth : 1;

                cursor = cursor.parent;
            }

            return -1;
        }

        bool HasMappedPhysicsBone(Transform physicsBone)
        {
            if (physicsBone == null)
                return false;

            for (var i = 0; i < _dualBindings.Count; i++)
            {
                var binding = _dualBindings[i];
                if (binding.PhysicsBone == physicsBone && binding.Body != null)
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

        static string[] GetContactRegionAliases(DebugHitContactRegion region)
        {
            return region switch
            {
                DebugHitContactRegion.Head => HeadAliases,
                DebugHitContactRegion.Chest => ChestAliases,
                DebugHitContactRegion.Hips => HipsAliases,
                DebugHitContactRegion.LeftArm => LeftArmAliases,
                DebugHitContactRegion.RightArm => RightArmAliases,
                DebugHitContactRegion.LeftElbow => LeftElbowAliases,
                DebugHitContactRegion.RightElbow => RightElbowAliases,
                DebugHitContactRegion.LeftWrist => LeftWristAliases,
                DebugHitContactRegion.RightWrist => RightWristAliases,
                DebugHitContactRegion.LeftLeg => LeftLegAliases,
                DebugHitContactRegion.RightLeg => RightLegAliases,
                DebugHitContactRegion.LeftKnee => LeftKneeAliases,
                DebugHitContactRegion.RightKnee => RightKneeAliases,
                DebugHitContactRegion.LeftAnkle => LeftAnkleAliases,
                DebugHitContactRegion.RightAnkle => RightAnkleAliases,
                _ => null
            };
        }

        static int GetAliasRank(string normalizedBoneName, IReadOnlyList<string> aliases)
        {
            if (string.IsNullOrEmpty(normalizedBoneName) || aliases == null)
                return -1;

            for (var i = 0; i < aliases.Count; i++)
            {
                var alias = aliases[i];
                if (string.IsNullOrWhiteSpace(alias))
                    continue;
                if (normalizedBoneName.Contains(alias, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        static string NormalizeBoneToken(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var chars = new char[raw.Length];
            var count = 0;
            for (var i = 0; i < raw.Length; i++)
            {
                var c = raw[i];
                if (!char.IsLetterOrDigit(c))
                    continue;
                chars[count++] = char.ToLowerInvariant(c);
            }

            return count <= 0 ? string.Empty : new string(chars, 0, count);
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

        Vector3 ResolveHeavyPartialContactPoint(
            int primaryBindingIndex,
            Vector3 fallbackContactPoint,
            Vector3 forceDirection)
        {
            if (primaryBindingIndex < 0 || primaryBindingIndex >= _dualBindings.Count)
                return fallbackContactPoint;

            var body = _dualBindings[primaryBindingIndex].Body;
            if (body == null)
                return fallbackContactPoint;

            var direction = forceDirection;
            if (direction.sqrMagnitude < 0.0001f)
                return fallbackContactPoint;
            direction.Normalize();

            var probeDistance = Mathf.Max(0.05f, heavyPartialContactProbeDistance);
            var sourceSideOrigin = body.worldCenterOfMass - direction * probeDistance;
            var maxDistance = probeDistance * 2f;

            // 优先用射线命中实际表面；失败时退到离攻击源侧最近的 Collider 点
            // Prefer a ray hit on the real surface; fall back to closest collider point from the source side
            if (TryRaycastAttachedColliders(body, sourceSideOrigin, direction, maxDistance, out var raycastPoint))
                return raycastPoint;
            if (TryFindClosestAttachedColliderPoint(body, sourceSideOrigin, out var closestPoint))
                return closestPoint;

            return fallbackContactPoint;
        }

        static bool TryRaycastAttachedColliders(
            Rigidbody body,
            Vector3 origin,
            Vector3 direction,
            float maxDistance,
            out Vector3 hitPoint)
        {
            hitPoint = default;
            var colliders = body.GetComponentsInChildren<Collider>();
            if (colliders == null || colliders.Length == 0)
                return false;

            var ray = new Ray(origin, direction);
            var bestDistance = float.MaxValue;
            var hasHit = false;
            for (var i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (!IsAttachedColliderForBody(collider, body))
                    continue;
                if (!collider.Raycast(ray, out var hit, maxDistance))
                    continue;
                if (hit.distance >= bestDistance)
                    continue;

                bestDistance = hit.distance;
                hitPoint = hit.point;
                hasHit = true;
            }

            return hasHit;
        }

        static bool TryFindClosestAttachedColliderPoint(
            Rigidbody body,
            Vector3 origin,
            out Vector3 closestPoint)
        {
            closestPoint = default;
            var colliders = body.GetComponentsInChildren<Collider>();
            if (colliders == null || colliders.Length == 0)
                return false;

            var bestDistanceSqr = float.MaxValue;
            var hasPoint = false;
            for (var i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (!IsAttachedColliderForBody(collider, body))
                    continue;

                var point = collider.ClosestPoint(origin);
                var distanceSqr = (point - origin).sqrMagnitude;
                if (distanceSqr >= bestDistanceSqr)
                    continue;

                bestDistanceSqr = distanceSqr;
                closestPoint = point;
                hasPoint = true;
            }

            return hasPoint;
        }

        static bool IsAttachedColliderForBody(Collider collider, Rigidbody body)
        {
            return collider != null
                && collider.enabled
                && collider.attachedRigidbody == body;
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

        void BeginHeavyPartialFullPhysicsSimulation()
        {
            for (var i = 0; i < _dualBindings.Count; i++)
            {
                var body = _dualBindings[i].Body;
                if (body == null)
                    continue;

                PreparePartialBodyRuntime(i, body);
                SetDynamic(body);
                body.WakeUp();
            }
        }

        void RestoreHeavyPartialFullPhysicsSimulation()
        {
            for (var i = 0; i < _dualBindings.Count; i++)
            {
                var body = _dualBindings[i].Body;
                if (body == null)
                    continue;

                RestorePartialBodyRuntime(i, body);
                SetKinematic(body);
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
            }

            RestoreHeavyPartialFullPhysicsSimulation();
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
            RestoreHeavyPartialFullPhysicsSimulation();
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
            RestoreHeavyPartialFullPhysicsSimulation();
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
                if (!ShouldIncludeBoneInRecoveryPose(binding.VisualBone))
                    continue;

                bones[count] = binding.VisualBone;
                // 恢复快照需转换到 Visual 骨骼父节点局部空间，避免双骨架根基准差导致起身前转圈
                // Convert physics world rotation into visual-parent local space to avoid recovery spin
                rotations[count] = ToVisualLocalRotation(binding.VisualBone, binding.PhysicsBone.rotation);
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

        static Quaternion ToVisualLocalRotation(Transform visualBone, Quaternion physicsWorldRotation)
        {
            if (visualBone == null)
                return physicsWorldRotation;

            var visualParent = visualBone.parent;
            if (visualParent == null)
                return physicsWorldRotation;

            return Quaternion.Inverse(visualParent.rotation) * physicsWorldRotation;
        }

        static bool ShouldIncludeBoneInRecoveryPose(Transform visualBone)
        {
            if (visualBone == null)
                return false;

            var boneName = visualBone.name;
            if (string.IsNullOrEmpty(boneName))
                return true;

            // 起身 PoseMatch 排除 hips/pelvis，避免把整体朝向扭到动画首帧造成“原地转圈”
            // Exclude hips/pelvis from recovery pose match to prevent whole-body spin-in-place
            return boneName.IndexOf("hip", StringComparison.OrdinalIgnoreCase) < 0
                   && boneName.IndexOf("pelvis", StringComparison.OrdinalIgnoreCase) < 0;
        }

        RagdollAnchor CaptureDualRecoveryAnchor()
        {
            var hips = FindBindingByNameContains("hip");
            if (hips.PhysicsBone == null)
                return new RagdollAnchor(Vector3.zero, transform.forward, false);

            var head = FindBindingByNameContains("head");
            var chest = FindBindingByNameContains("chest");
            if (chest.PhysicsBone == null)
                chest = FindBindingByNameContains("spine");

            var hipsPos = hips.PhysicsBone.position;
            var planarForward = ResolveRecoveryFacingForward(
                head.PhysicsBone,
                chest.PhysicsBone,
                hips.PhysicsBone,
                transform.forward);

            return new RagdollAnchor(hipsPos, planarForward.normalized, true);
        }

        Vector3 ResolveRecoveryFacingForward(
            Transform headBone,
            Transform chestBone,
            Transform hipsBone,
            Vector3 fallbackForward)
        {
            // 起身朝向轴优先使用 hips->head，保证倒地纵轴与起身朝向一致
            // Prefer hips->head axis so get-up heading follows fallen body longitudinal axis
            if (headBone != null && hipsBone != null)
            {
                var hipsToHead = headBone.position - hipsBone.position;
                if (TryProjectToGroundPlane(hipsToHead, out var headAxisForward))
                    return headAxisForward;
            }

            if (TryProjectToGroundPlane(chestBone != null ? chestBone.forward : Vector3.zero, out var planarForward))
                return planarForward;

            // 当 torso.forward 接近竖直时，用 torso.up 的平面投影作为朝向代理，减少朝向随机跳变
            // When torso.forward is near vertical, use torso.up planar projection as facing proxy
            if (TryProjectToGroundPlane(chestBone != null ? chestBone.up : Vector3.zero, out planarForward))
                return planarForward;

            if (TryProjectToGroundPlane(hipsBone != null ? hipsBone.forward : Vector3.zero, out planarForward))
                return planarForward;

            if (TryProjectToGroundPlane(fallbackForward, out planarForward))
                return planarForward;

            return Vector3.forward;
        }

        static bool TryProjectToGroundPlane(Vector3 direction, out Vector3 planarDirection)
        {
            planarDirection = direction;
            planarDirection.y = 0f;
            if (planarDirection.sqrMagnitude < 0.0001f)
                return false;

            planarDirection.Normalize();
            return true;
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
