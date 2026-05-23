using System;
using System.Collections.Generic;
using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 角色控制器根 — 层次状态机、子模块初始化与调度唯一入口
    /// Character controller root — HSM, module init, sole dispatcher
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UnityEngine.CharacterController))]
    public sealed class CharacterControllerRoot : MonoBehaviour
    {
        [Header("Config / 配置")]
        [SerializeField] CharacterControllerConfig config;

        [Header("Animation Config / 动画配置")]
        [SerializeField] CharacterAnimationConfig animationConfig;

        [Header("Locomotion / 移动")]
        [Tooltip("留空则使用本物体上的 Unity CharacterController — Uses CC on this GameObject if null")]
        [SerializeField] UnityEngine.CharacterController unityCharacterController;

        [Header("Animation / 动画")]
        [Tooltip("挂在 Model 子物体上；移动转向只旋转此物体 — On Model child; facing rotates this transform only")]
        [SerializeField] Animator animator;

        [Header("Ragdoll Legacy Fallback / 布娃娃旧回退配置")]
        [Tooltip("仅 legacy 回退路径使用；留空则从 Animator 所在物体向下搜索 — Legacy fallback only; search under Animator transform if null")]
        [SerializeField] Transform ragdollSearchRoot;
        [Tooltip("仅 legacy 回退路径使用的刚体数组 — Rigidbody array used by legacy fallback path only")]
        [SerializeField] Rigidbody[] ragdollBodies;
        [Tooltip("仅 legacy 回退时自动收集刚体 — Auto-collect only when legacy fallback is needed")]
        [SerializeField] bool autoCollectRagdollIfEmpty = true;
        [Tooltip("legacy 回退：无 Ragdoll 刚体时的击倒占位时长 — Legacy fallback placeholder knockdown duration")]
        [SerializeField] float placeholderSettleDuration = 1.5f;
        [Tooltip("legacy 回退：击倒最小时长（秒）— Legacy fallback minimum knockdown duration")]
        [SerializeField] float knockdownMinDuration = 0.8f;
        [Tooltip("legacy 回退：沉降速度阈值 — Legacy fallback settle speed threshold")]
        [SerializeField] float settleSpeedThreshold = 0.15f;
        [Tooltip("Ragdoll 外观组件；留空则运行时自动使用本物体组件 — Ragdoll facade; uses component on this GameObject if null")]
        [SerializeField] CharacterRagdollSystem ragdollSystem;

        [Header("Recovery / 起身")]
        [SerializeField] Transform spineTransform;
        [Tooltip("胸口朝向参考（建议 Spine2）；空则用 Spine — Torso facing for recoil; default Spine if null")]
        [SerializeField] Transform torsoFacingTransform;
        [Tooltip("颈部弯曲施加骨（可选）— Neck bone for procedural recoil")]
        [SerializeField] Transform neckRecoilTransform;
        [Tooltip("头部弯曲施加骨（建议 Head）— Head bone for procedural recoil")]
        [SerializeField] Transform headRecoilTransform;
        [SerializeField] float faceUpDotThreshold;
        [Tooltip("进入 Recovering 前是否预先旋转到目标朝向；默认关闭以避免地面扭身感 — Pre-rotate before Recovering; off by default")]
        [SerializeField] bool preAlignRotationBeforeRecovering = false;
        [Tooltip("仰躺起身自动翻转阈值（度）；仅当候选朝向与当前朝向夹角超过此值时才翻转 180° — Auto flip threshold for Back get-up")]
        [SerializeField] float backGetUpAutoFlipAngleThreshold = 135f;

        CharacterMotor _locomotion;
        CharacterAnimationPresenter _animation;
        CharacterCombat _combat;
        CharacterRecoveryFlow _recovery;

        readonly List<ICharacterModule> _modules = new();
        readonly CharacterContext _context = new();
        readonly CharacterStateMachine _hsm = new();

        bool _initialized;
        bool _lightFlinchOverlayActive;
        float _lightFlinchOverlayTime;
        Vector3 _initialRootMinusHipsOffset;
        bool _hasInitialRootMinusHipsOffset;
        bool _hasLastDebugHit;
        float _lastDebugHitTime = -1f;
        HitContext _lastDebugHit;

        public CharacterState CurrentState => _hsm.CurrentState;

        /// <summary>
        /// 地面轻击 Flinch overlay 是否进行中（不切 HSM）
        /// Whether grounded light-hit flinch overlay is active
        /// </summary>
        public bool IsLightFlinchOverlayActive => _lightFlinchOverlayActive;
        public CharacterSuperstate CurrentSuperstate => _hsm.Capabilities.Superstate;
        public CharacterContext Context => _context;
        public CharacterControllerConfig Config => config;
        public CharacterAnimationConfig AnimationConfig => animationConfig;
        public CharacterStateMachine StateMachine => _hsm;
        public bool IsRagdollSettled => ragdollSystem == null || ragdollSystem.IsSettled;
        public bool IsUsingDualRagdoll => ragdollSystem != null && ragdollSystem.IsUsingDualSkeleton;
        public bool IsUsingLegacyRagdollFallback => ragdollSystem != null && ragdollSystem.IsUsingLegacyFallback;
        public string RagdollBackendStatus => ragdollSystem != null ? ragdollSystem.BackendStatus : "Unavailable";
        public CharacterRagdollMode CurrentRagdollMode => ragdollSystem != null ? ragdollSystem.Mode : CharacterRagdollMode.Animated;
        public string CurrentRagdollChain => ragdollSystem != null ? ragdollSystem.ActiveChainName : "None";
        public int RagdollMappedBoneCount => ragdollSystem != null ? ragdollSystem.MappedBoneCount : 0;
        public bool HasRagdollDebugHitInfo => ragdollSystem != null && ragdollSystem.HasDebugHitInfo;
        public float RagdollDebugLastHitTime => ragdollSystem != null ? ragdollSystem.DebugLastHitTime : -1f;
        public Vector3 RagdollDebugImpulsePoint => ragdollSystem != null ? ragdollSystem.DebugImpulsePoint : transform.position;
        public Vector3 RagdollDebugImpulseDirection => ragdollSystem != null ? ragdollSystem.DebugImpulseDirection : transform.forward;
        public Transform RagdollDebugPrimaryAffectedBone => ragdollSystem != null ? ragdollSystem.DebugPrimaryAffectedBone : null;
        public IReadOnlyList<Transform> RagdollDebugAffectedBones => ragdollSystem != null ? ragdollSystem.DebugAffectedBones : Array.Empty<Transform>();
        public bool HasLastDebugHit => _hasLastDebugHit;
        public float LastDebugHitTime => _lastDebugHitTime;
        public HitContext LastDebugHit => _lastDebugHit;
        public bool IsInKnockdownPhase =>
            _hsm.CurrentState is CharacterState.Knockdown or CharacterState.ForcedKnockdown;
        public bool IsInRecoveryPhase => _hsm.CurrentState == CharacterState.Recovering;

        /// <summary>
        /// 是否接受移动输入（与 HSM CanMove 一致）
        /// Whether move input is accepted — mirrors HSM CanMove
        /// </summary>
        public bool AllowsLocomotionInput => _hsm.Capabilities.CanMove;

        /// <summary>
        /// 是否可发起拔刀/收刀（仅 Locomotion）
        /// Whether equip toggle can start — typically Locomotion only
        /// </summary>
        public bool CanToggleWeapon => _hsm.Capabilities.CanToggleWeapon;

        void Awake()
        {
            InitializeModules();
        }

        /// <summary>
        /// 从 ragdollSearchRoot（或 Animator/自身）子层级收集全部 Rigidbody，按名称排序
        /// Collect all child Rigidbodies under search root, sorted by name
        /// </summary>
        public void CollectRagdollBodiesFromChildren()
        {
            var searchRoot = ragdollSearchRoot != null
                ? ragdollSearchRoot
                : animator != null
                    ? animator.transform
                    : transform;

            var list = new List<Rigidbody>();
            foreach (var rb in searchRoot.GetComponentsInChildren<Rigidbody>(true))
            {
                if (rb == null)
                    continue;
                if (rb.transform == transform)
                    continue;
                if (rb.GetComponent<UnityEngine.CharacterController>() != null)
                    continue;
                list.Add(rb);
            }

            list.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
            ragdollBodies = list.ToArray();

            Debug.Log(
                $"[CharacterControllerRoot] 已收集 {ragdollBodies.Length} 个 Rigidbody（搜索根: {searchRoot.name}）\n" +
                $"[CharacterControllerRoot] Collected {ragdollBodies.Length} rigidbodies under '{searchRoot.name}'.",
                this);
        }

#if UNITY_EDITOR
        [ContextMenu("Collect Ragdoll Rigidbodies / 收集布娃娃刚体")]
        void EditorCollectRagdollBodies()
        {
            CollectRagdollBodiesFromChildren();
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        /// <summary>
        /// 在 Awake 中构造所有子模块（普通 C# 类，非 MonoBehaviour）
        /// Construct all sub-modules in Awake (plain C# classes, not MonoBehaviour)
        /// </summary>
        void InitializeModules()
        {
            if (unityCharacterController == null)
                unityCharacterController = GetComponent<UnityEngine.CharacterController>();

            if (config == null)
            {
                Debug.LogError(
                    "[CharacterControllerRoot] 未分配 CharacterControllerConfig（Config 分组）\n" +
                    "[CharacterControllerRoot] CharacterControllerConfig not assigned (Config section).",
                    this);
                return;
            }

            if (animationConfig == null)
            {
                Debug.LogError(
                    "[CharacterControllerRoot] 未分配 CharacterAnimationConfig（Animation Config 分组）\n" +
                    "[CharacterControllerRoot] CharacterAnimationConfig not assigned (Animation Config section).",
                    this);
                return;
            }

            if (unityCharacterController == null)
            {
                Debug.LogError(
                    "[CharacterControllerRoot] 缺少 Unity CharacterController（Locomotion 分组）\n" +
                    "[CharacterControllerRoot] Missing Unity CharacterController (Locomotion section).",
                    this);
                return;
            }

            var rotationTarget = animator != null ? animator.transform : transform;
            _locomotion = new CharacterMotor(
                transform,
                unityCharacterController,
                config,
                rotationTarget);
            _animation = new CharacterAnimationPresenter(
                animator,
                config,
                animationConfig,
                spineTransform,
                torsoFacingTransform,
                neckRecoilTransform,
                headRecoilTransform);
            _combat = new CharacterCombat(config);
            if (ragdollSystem == null)
                ragdollSystem = GetComponent<CharacterRagdollSystem>();
            if (ragdollSystem == null)
                ragdollSystem = gameObject.AddComponent<CharacterRagdollSystem>();

            if (autoCollectRagdollIfEmpty && (ragdollBodies == null || ragdollBodies.Length == 0))
            {
                var shouldCollectLegacyBodies = ragdollSystem == null || !ragdollSystem.CanAttemptDualSkeletonSetup(transform);
                if (ragdollSystem != null && !ragdollSystem.IsLegacyFallbackAllowed)
                    shouldCollectLegacyBodies = false;
                if (shouldCollectLegacyBodies)
                {
                    CollectRagdollBodiesFromChildren();
                }
#if UNITY_EDITOR
                else
                {
                    Debug.Log(
                        "[CharacterControllerRoot] 检测到双骨架配置，跳过 legacy ragdollBodies 自动收集。\n" +
                        "[CharacterControllerRoot] Dual-skeleton setup detected, skipping legacy ragdollBodies auto-collect.",
                        this);
                }
#endif
            }

            ragdollSystem.InitializeRuntime(
                transform,
                ragdollBodies,
                config,
                animationConfig,
                knockdownMinDuration,
                placeholderSettleDuration,
                settleSpeedThreshold,
                animator != null ? animator.transform : transform);
            _recovery = new CharacterRecoveryFlow(
                torsoFacingTransform != null ? torsoFacingTransform : spineTransform,
                spineTransform,
                faceUpDotThreshold);

            _modules.Clear();
            _modules.Add(_locomotion);
            _modules.Add(_animation);
            _modules.Add(_combat);
            _modules.Add(ragdollSystem);
            _modules.Add(_recovery);

            _hsm.ResetToLocomotion();

            foreach (var module in _modules)
                module.OnEnterState(CharacterState.Locomotion, default);

            CacheInitialRecoveryOffsets();
            _initialized = true;
            SyncContext();
        }

        /// <summary>
        /// 供 PlayerInputReader 转发移动输入
        /// For PlayerInputReader to forward move input
        /// </summary>
        public void SetMoveInput(Vector2 input)
        {
            if (!_initialized || _locomotion == null)
                return;
            _locomotion.SetMoveInput(input);
        }

        void Update()
        {
            if (!_initialized)
                return;

            var dt = Time.deltaTime;
            _hsm.Tick(dt);

            _combat?.TickBalance(dt);

            if (AllowsLocomotionInput)
            {
                var weaponEquipped = _animation != null && _animation.IsWeaponEquipped;
                _locomotion?.TickMovement(
                    dt,
                    AllowsLocomotionInput,
                    _hsm.CurrentState,
                    weaponEquipped);
            }

            foreach (var module in _modules)
                module.OnTickState(_hsm.CurrentState, dt);

            TickLightFlinchOverlay(dt);

            if (_hsm.CurrentState == CharacterState.LightFlinch)
                _animation?.UpdateLightFlinchOverlay(_hsm.TimeInState);

            UpdateLocomotionAnimation();
            TickStateTransitions();
            SyncContext();
        }

        void FixedUpdate()
        {
            if (!_initialized)
                return;

            var fdt = Time.fixedDeltaTime;
            foreach (var module in _modules)
                module.OnFixedTickState(_hsm.CurrentState, fdt);
        }

        /// <summary>
        /// 轻击脊柱弯曲在 Animator 之后写入
        /// Spine recoil after animator update
        /// </summary>
        void LateUpdate()
        {
            if (!_initialized || _animation == null)
                return;

            ragdollSystem?.LateTick(Time.deltaTime);

            if (_hsm.CurrentState == CharacterState.Recovering)
                _animation.TickRecoveryPoseMatchLateUpdate(Time.deltaTime);

            if (_hsm.CurrentState == CharacterState.HeavyStagger)
                ragdollSystem?.ApplyHeavyPartialPoseLateUpdate();

            if (!_lightFlinchOverlayActive)
                return;

            _animation.ApplySpineRecoilLateUpdate(_lightFlinchOverlayTime);

            // 在最后一帧弯曲淡出后再结束，避免 Update 里过早 End 导致硬弹回
            // End overlay after final LateUpdate fade, not mid-Update
            if (config != null && _lightFlinchOverlayTime >= config.lightFlinchDuration)
                EndLightFlinchOverlay();
        }

        void UpdateLocomotionAnimation()
        {
            if (_animation == null || _locomotion == null || config == null)
                return;

            if (!AllowsLocomotionInput && _hsm.CurrentState != CharacterState.Recovering)
                return;

            var weaponEquipped = _animation.IsWeaponEquipped;
            var speed = _locomotion.WorldVelocity.magnitude;
            var maxSpeed = config.GetMaxMoveSpeed(weaponEquipped);
            var normalized = maxSpeed > 0.01f
                ? Mathf.Clamp01(speed / maxSpeed)
                : 0f;
            _animation.SyncLocomotionAnimator(normalized);
        }

        /// <summary>
        /// 切换装备/收回武器（由 PlayerInputReader Equip 键调用）
        /// Toggle weapon equip/unequip (Equip input action)
        /// </summary>
        public bool TryToggleWeaponEquip()
        {
            if (!_initialized || _animation == null)
                return false;

            if (!CanToggleWeapon)
                return false;

            var speed = _locomotion != null ? _locomotion.WorldVelocity.magnitude : 0f;
            var moving = animationConfig != null && speed >= animationConfig.movingSpeedThreshold;

            if (!_animation.TryToggleWeaponEquip(moving))
            {
#if UNITY_EDITOR
                if (_animation.IsEquipPlaybackInProgress)
                {
                    Debug.Log(
                        "[CharacterControllerRoot] 装备/收回动画播放中，忽略本次 Equip 键\n" +
                        "[CharacterControllerRoot] Equip input ignored while weapon playback in progress.",
                        this);
                }
#endif
                return false;
            }

            return TransitionTo(CharacterState.WeaponEquipPlayback, default, recordHit: false);
        }

        void TickStateTransitions()
        {
            switch (_hsm.CurrentState)
            {
                case CharacterState.WeaponEquipPlayback:
                    if (_animation != null && !_animation.IsEquipPlaybackInProgress)
                        TransitionTo(CharacterState.Locomotion, default, recordHit: false);
                    break;

                case CharacterState.LightFlinch:
                    if (config != null && _hsm.TimeInState >= config.lightFlinchDuration)
                        TransitionTo(CharacterState.Locomotion, default, recordHit: false);
                    break;

                case CharacterState.HeavyStagger:
                    if (config != null &&
                        _hsm.TimeInState >= config.heavyBlendBackDuration &&
                        (ragdollSystem == null || ragdollSystem.IsHeavyPartialBlendComplete))
                        TransitionTo(CharacterState.Locomotion, default, recordHit: false);
                    break;

                case CharacterState.Knockdown:
                case CharacterState.ForcedKnockdown:
                    if (ragdollSystem != null && ragdollSystem.IsSettled)
                    {
                        var recoveryPose = ragdollSystem.CaptureRecoveryPose();
                        var recoveryAnchor = ragdollSystem.CaptureRecoveryAnchor();
                        var getUpType = ragdollSystem != null
                            ? ragdollSystem.EvaluateGetUpType()
                            : _recovery != null
                                ? _recovery.EvaluateGetUpType()
                                : RecoveryGetUpType.Back;

                        var currentForward = ResolveCurrentVisualForward();
                        if (!recoveryAnchor.IsValid)
                        {
#if UNITY_EDITOR
                            Debug.LogWarning(
                                "[CharacterControllerRoot] Recovery anchor 无效，使用当前根位置与朝向回退。\n" +
                                "[CharacterControllerRoot] Recovery anchor invalid; fallback to current root position/forward.",
                                this);
#endif
                            recoveryAnchor = new RagdollAnchor(transform.position, currentForward, true);
                        }
                        AlignRootPositionToRecoveryAnchor(in recoveryAnchor);
                        var resolvedForward = ResolveRecoveryForward(currentForward, recoveryAnchor.FacingForward, getUpType, out var shouldFlipBack, out var angleToAnchor);
                        if (preAlignRotationBeforeRecovering)
                            ApplyRecoveryVisualRotation(resolvedForward);

#if UNITY_EDITOR
                        if (recoveryAnchor.IsValid)
                        {
                            var currentYaw = Mathf.Atan2(currentForward.x, currentForward.z) * Mathf.Rad2Deg;
                            var anchorYaw = Mathf.Atan2(recoveryAnchor.FacingForward.x, recoveryAnchor.FacingForward.z) * Mathf.Rad2Deg;
                            Debug.Log(
                                $"[RecoveryAlign] preAlignRotation={preAlignRotationBeforeRecovering}, getUpType={getUpType}, " +
                                $"currentYaw={currentYaw:F1}, anchorYaw={anchorYaw:F1}, angle={angleToAnchor:F1}, autoFlipBack={shouldFlipBack}",
                                this);
                        }
#endif

                        _recovery?.BeginRecovery(getUpType, ResolveRecoveryFallbackDuration(getUpType));
                        _animation?.SetPendingRecoveryType(getUpType);
                        if (!recoveryPose.IsValid)
                        {
#if UNITY_EDITOR
                            Debug.LogWarning(
                                "[CharacterControllerRoot] Recovery pose 无效，跳过 PoseMatch，直接进入 GetUp 动画。\n" +
                                "[CharacterControllerRoot] Recovery pose invalid; skipping pose match and entering get-up playback.",
                                this);
#endif
                        }
                        _animation?.SetPendingRecoveryPoseSnapshot(in recoveryPose);
                        TransitionTo(CharacterState.Recovering, _context.LastHit, recordHit: false);
                    }
                    break;

                case CharacterState.Recovering:
                    if (_recovery != null && _recovery.IsComplete)
                    {
                        _combat?.ResetBalance();
                        TransitionTo(CharacterState.Locomotion, default, recordHit: false);
                    }
                    break;
            }
        }

        public void ReceiveHit(HitContext hitContext)
        {
            if (!_initialized || IsHitImmune())
                return;

            _hasLastDebugHit = true;
            _lastDebugHitTime = Time.time;
            _lastDebugHit = hitContext;
            _context.LastHit = hitContext;

            CharacterState target;

            if (hitContext.BypassBalance
                || hitContext.Type == HitType.ForceKnockdownLight
                || hitContext.Type == HitType.ForceKnockdownHeavy)
            {
                _combat?.ForceDepleteBalance();
                target = CharacterState.ForcedKnockdown;
            }
            else
            {
                var depleted = _combat != null && _combat.ApplyBalanceDamage(in hitContext);
                target = CharacterStateMachine.ResolveHitTarget(hitContext, depleted);
            }

            if (TryApplyGroundedLightHitOverlay(in hitContext, target))
                return;

            EndLightFlinchOverlay();

            if (_hsm.ShouldAbortWeaponOnTransition(target))
                _animation?.AbortWeaponEquipPlayback();

            TransitionTo(target, hitContext);
        }

        /// <summary>
        /// 地面轻击：保持 Locomotion / WeaponEquipPlayback，仅 overlay，不 Abort 装备
        /// Grounded light hit — overlay only, preserve equip playback
        /// </summary>
        bool TryApplyGroundedLightHitOverlay(in HitContext hitContext, CharacterState resolvedTarget)
        {
            if (resolvedTarget != CharacterState.LightFlinch)
                return false;

            var state = _hsm.CurrentState;
            if (state != CharacterState.Locomotion && state != CharacterState.WeaponEquipPlayback)
                return false;

            BeginLightFlinchOverlay(in hitContext);
            return true;
        }

        void BeginLightFlinchOverlay(in HitContext hitContext)
        {
            _lightFlinchOverlayTime = 0f;
            _lightFlinchOverlayActive = true;
            _animation?.BeginLightFlinchOverlay(in hitContext);

            if (config != null && config.lightFlinchRootKnockbackDistance > 0f)
                _locomotion?.ApplyLightHitRootKnockback(in hitContext, transform);

            SyncContext();
        }

        void EndLightFlinchOverlay()
        {
            if (!_lightFlinchOverlayActive)
                return;

            _lightFlinchOverlayActive = false;
            _lightFlinchOverlayTime = 0f;
            _animation?.EndLightFlinchOverlay();
            SyncContext();
        }

        void TickLightFlinchOverlay(float deltaTime)
        {
            if (!_lightFlinchOverlayActive)
                return;

            _lightFlinchOverlayTime += deltaTime;
            _animation?.UpdateLightFlinchOverlay(_lightFlinchOverlayTime);
        }

        bool IsHitImmune() => !_hsm.Capabilities.CanReceiveHit;

        bool TransitionTo(CharacterState newState, HitContext hitContext, bool recordHit = true)
        {
            var oldState = _hsm.CurrentState;

            if (!_hsm.TryTransition(newState, out var reason))
            {
#if UNITY_EDITOR
                if (!string.IsNullOrEmpty(reason))
                {
                    Debug.Log(
                        $"[CharacterControllerRoot] 状态迁移被拒绝: {reason}\n" +
                        $"[CharacterControllerRoot] Transition rejected: {reason}",
                        this);
                }
#endif
                return false;
            }

            if (recordHit)
                _context.LastHit = hitContext;

            foreach (var module in _modules)
                module.OnExitState(oldState);

            foreach (var module in _modules)
                module.OnEnterState(newState, in hitContext);

            SyncContext();
            return true;
        }

        void SyncContext()
        {
            var caps = _hsm.Capabilities;
            _context.State = _hsm.CurrentState;
            _context.Superstate = caps.Superstate;
            _context.StateTime = _hsm.TimeInState;
            _context.CanMove = caps.CanMove;
            _context.CanToggleWeapon = caps.CanToggleWeapon;
            _context.WorldVelocity = _locomotion != null ? _locomotion.WorldVelocity : Vector3.zero;
            _context.CurrentBalance = _combat != null ? _combat.CurrentBalance : 0;
            _context.MaxBalance = _combat != null ? _combat.MaxBalance : 6;
            _context.IsWeaponEquipped = _animation != null && _animation.IsWeaponEquipped;
            _context.IsLightFlinchOverlayActive = _lightFlinchOverlayActive;
        }

        void CacheInitialRecoveryOffsets()
        {
            if (!TryGetHumanoidHips(out var hips))
            {
                _hasInitialRootMinusHipsOffset = false;
                return;
            }

            _initialRootMinusHipsOffset = transform.position - hips.position;
            _hasInitialRootMinusHipsOffset = true;
        }

        bool TryGetHumanoidHips(out Transform hips)
        {
            hips = null;
            if (animator == null || !animator.isHuman || animator.avatar == null)
                return false;

            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            return hips != null;
        }

        /// <summary>
        /// 相机代理目标：优先返回 Humanoid Hips，失败时回退到模型或根节点
        /// Camera proxy target: prefer humanoid hips, fallback to model/root transform
        /// </summary>
        public Transform GetCameraFollowAnchor()
        {
            if (IsInKnockdownPhase && ragdollSystem != null && ragdollSystem.CameraFollowAnchor != null)
                return ragdollSystem.CameraFollowAnchor;
            if (TryGetHumanoidHips(out var hips))
                return hips;
            if (animator != null)
                return animator.transform;
            return transform;
        }

        void AlignRootPositionToRecoveryAnchor(in RagdollAnchor anchor)
        {
            if (!anchor.IsValid)
                return;

            var targetRootPosition = anchor.HipsWorldPosition;
            if (_hasInitialRootMinusHipsOffset)
                targetRootPosition += _initialRootMinusHipsOffset;
            else
                targetRootPosition.y = transform.position.y;

            // 防止直接使用 ragdoll hips 的低位 Y 导致起身开场沉地
            // Avoid sinking: align by XZ and solve a grounded Y for CharacterController
            targetRootPosition.y = ResolveRecoveryRootY(targetRootPosition, anchor.HipsWorldPosition.y);

            var ccWasEnabled = unityCharacterController != null && unityCharacterController.enabled;
            if (ccWasEnabled)
                unityCharacterController.enabled = false;

            transform.position = targetRootPosition;

            if (ccWasEnabled)
                unityCharacterController.enabled = true;
        }

        Vector3 ResolveCurrentVisualForward()
        {
            var forward = animator != null ? animator.transform.forward : transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        Vector3 ResolveRecoveryForward(
            Vector3 currentForward,
            Vector3 anchorForward,
            RecoveryGetUpType getUpType,
            out bool shouldFlipBack,
            out float angleToAnchor)
        {
            var normalizedCurrent = currentForward;
            normalizedCurrent.y = 0f;
            if (normalizedCurrent.sqrMagnitude < 0.0001f)
                normalizedCurrent = transform.forward;
            normalizedCurrent.y = 0f;
            normalizedCurrent = normalizedCurrent.sqrMagnitude > 0.0001f ? normalizedCurrent.normalized : Vector3.forward;

            var normalizedAnchor = anchorForward;
            normalizedAnchor.y = 0f;
            if (normalizedAnchor.sqrMagnitude < 0.0001f)
                normalizedAnchor = normalizedCurrent;
            normalizedAnchor = normalizedAnchor.normalized;

            angleToAnchor = Vector3.Angle(normalizedCurrent, normalizedAnchor);
            shouldFlipBack = false;

            if (getUpType == RecoveryGetUpType.Back)
            {
                var threshold = Mathf.Clamp(backGetUpAutoFlipAngleThreshold, 0f, 180f);
                shouldFlipBack = angleToAnchor >= threshold;
            }

            return shouldFlipBack ? -normalizedAnchor : normalizedAnchor;
        }

        void ApplyRecoveryVisualRotation(Vector3 resolvedForward)
        {
            if (animator == null)
                return;

            var planarForward = resolvedForward;
            planarForward.y = 0f;
            if (planarForward.sqrMagnitude < 0.0001f)
                return;

            animator.transform.rotation = Quaternion.LookRotation(planarForward.normalized, Vector3.up);
        }

        float ResolveRecoveryRootY(Vector3 targetRootPosition, float hipsWorldY)
        {
            var fallbackY = transform.position.y;
            if (unityCharacterController == null)
                return fallbackY;

            var rayOrigin = new Vector3(
                targetRootPosition.x,
                Mathf.Max(hipsWorldY + 1.5f, fallbackY + 1.5f),
                targetRootPosition.z);
            if (!TryFindGroundHit(rayOrigin, 6f, out var hit))
                return fallbackY;

            var halfHeight = unityCharacterController.height * 0.5f;
            var solvedY = hit.point.y - unityCharacterController.center.y + halfHeight + unityCharacterController.skinWidth;
            return solvedY;
        }

        bool TryFindGroundHit(Vector3 origin, float maxDistance, out RaycastHit groundHit)
        {
            var hits = Physics.RaycastAll(
                origin,
                Vector3.down,
                maxDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            if (hits == null || hits.Length == 0)
            {
                groundHit = default;
                return false;
            }

            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (var i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                var hitTransform = hit.collider != null ? hit.collider.transform : null;
                if (hitTransform != null && hitTransform.IsChildOf(transform))
                    continue;

                groundHit = hit;
                return true;
            }

            groundHit = default;
            return false;
        }

        float ResolveRecoveryFallbackDuration(RecoveryGetUpType getUpType)
        {
            if (animationConfig == null)
                return 2f;

            // 兜底解锁时长需要覆盖：PoseMatch + CrossFade + 目标起身时长，并留足缓冲
            // Fallback unlock duration must cover PoseMatch + CrossFade + target get-up duration + buffer
            var targetDuration = getUpType == RecoveryGetUpType.Back
                ? animationConfig.getUpBackTargetDuration
                : animationConfig.getUpFrontTargetDuration;
            var poseMatchDuration = Mathf.Max(0f, animationConfig.recoveryPoseMatchDuration);
            var crossFadeDuration = Mathf.Max(0f, animationConfig.getUpCrossFadeDuration);
            return Mathf.Max(0.2f, targetDuration) + poseMatchDuration + crossFadeDuration + 0.8f;
        }

        #region Animation event notify / 动画事件通知（由 AnimationEventReceiver 调用）

        /// <summary>
        /// 抓到武器帧：逻辑已装备 + 武器 Mesh；Equipped Bool 由 CharacterAnimationPresenter 按 Config 决定何时写入
        /// Grab weapon frame — logic equipped + mesh; Equipped bool timing from CharacterAnimationPresenter config
        /// </summary>
        public void NotifyWeaponEquipped()
        {
            if (!_initialized)
                return;

            _animation?.SetWeaponEquippedState(true);
            SyncContext();
        }

        /// <summary>
        /// 装备动画末帧：仅归零 FullBody/UpBody
        /// Equip clip end — reset overlay layers only
        /// </summary>
        public void NotifyWeaponEquipPlaybackFinished()
        {
            if (!_initialized)
                return;

            _animation?.CompleteWeaponEquipPlayback();
        }

        /// <summary>
        /// 收回至背上帧（与 Mesh 同步）：未装备状态
        /// Weapon on back frame — unequipped state
        /// </summary>
        public void NotifyWeaponUnequipped()
        {
            if (!_initialized)
                return;

            _animation?.SetWeaponEquippedState(false);
            SyncContext();
        }

        /// <summary>
        /// 收回动画末帧：仅归零 overlay 层
        /// Unequip clip end — reset overlay layers only
        /// </summary>
        public void NotifyWeaponUnequipPlaybackFinished()
        {
            if (!_initialized)
                return;

            _animation?.CompleteWeaponEquipPlayback();
        }

        /// <summary>
        /// 起身动画末帧事件：标记 Recovery 完成
        /// Get-up animation end event: mark recovery complete
        /// </summary>
        public void NotifyGetUpFinished()
        {
            if (!_initialized)
                return;
            if (_hsm.CurrentState != CharacterState.Recovering)
                return;

            _recovery?.MarkGetUpFinished();
        }

        #endregion

        #region Debug API / 调试 API

        public void DebugHitLight(HitDirection direction) =>
            DebugHitLight(HitDirectionUtility.ToWorldVector(direction, transform));

        public void DebugHitLight(HitDirection direction, DebugHitContactRegion contactRegion) =>
            DebugHitLight(HitDirectionUtility.ToWorldVector(direction, transform), GetDebugContactPoint(contactRegion));

        /// <summary>
        /// 调试轻击（连续世界来向）
        /// Debug light hit using continuous world incoming direction
        /// </summary>
        public void DebugHitLight(Vector3 worldIncomingDirection)
        {
            var incoming = worldIncomingDirection;
            incoming.y = 0f;
            if (incoming.sqrMagnitude < 0.0001f)
                incoming = transform.forward;
            incoming.Normalize();

            var dir = HitDirectionUtility.ResolveDirectionFromWorld(incoming, transform);
            ReceiveHit(new HitContext(
                HitType.Light,
                dir,
                transform.position + Vector3.up,
                impulse: 1f,
                source: null,
                worldIncomingDirection: incoming));
        }

        public void DebugHitLight(Vector3 worldIncomingDirection, Vector3 contactPoint)
        {
            var incoming = worldIncomingDirection;
            incoming.y = 0f;
            if (incoming.sqrMagnitude < 0.0001f)
                incoming = transform.forward;
            incoming.Normalize();

            var dir = HitDirectionUtility.ResolveDirectionFromWorld(incoming, transform);
            ReceiveHit(new HitContext(
                HitType.Light,
                dir,
                contactPoint,
                impulse: 1f,
                source: null,
                worldIncomingDirection: incoming));
        }

        public void DebugHitHeavy(HitDirection direction) =>
            DebugHitHeavy(-HitDirectionUtility.ToWorldVector(direction, transform));

        public void DebugHitHeavy(HitDirection direction, DebugHitContactRegion contactRegion) =>
            DebugHitHeavy(-HitDirectionUtility.ToWorldVector(direction, transform), GetDebugContactPoint(contactRegion));

        public void DebugHitHeavy(HitDirection direction, float impulseOverride, DebugHitContactRegion contactRegion) =>
            DebugHitHeavy(-HitDirectionUtility.ToWorldVector(direction, transform), impulseOverride, GetDebugContactPoint(contactRegion));

        /// <summary>
        /// 调试重击（连续世界来向）
        /// Debug heavy hit using continuous world incoming direction
        /// </summary>
        public void DebugHitHeavy(Vector3 worldIncomingDirection)
        {
            var incoming = worldIncomingDirection;
            incoming.y = 0f;
            if (incoming.sqrMagnitude < 0.0001f)
                incoming = transform.forward;
            incoming.Normalize();

            var dir = HitDirectionUtility.ResolveDirectionFromWorld(incoming, transform);
            ReceiveHit(new HitContext(
                HitType.Heavy,
                dir,
                transform.position + Vector3.up * 1.2f,
                impulse: 2f,
                source: null,
                worldIncomingDirection: incoming));
        }

        public void DebugHitHeavy(Vector3 worldIncomingDirection, float impulseOverride)
        {
            var incoming = worldIncomingDirection;
            incoming.y = 0f;
            if (incoming.sqrMagnitude < 0.0001f)
                incoming = transform.forward;
            incoming.Normalize();

            var dir = HitDirectionUtility.ResolveDirectionFromWorld(incoming, transform);
            ReceiveHit(new HitContext(
                HitType.Heavy,
                dir,
                transform.position + Vector3.up * 1.2f,
                impulse: Mathf.Max(0f, impulseOverride),
                source: null,
                worldIncomingDirection: incoming));
        }

        public void DebugHitHeavy(Vector3 worldIncomingDirection, Vector3 contactPoint)
        {
            var incoming = worldIncomingDirection;
            incoming.y = 0f;
            if (incoming.sqrMagnitude < 0.0001f)
                incoming = transform.forward;
            incoming.Normalize();

            var dir = HitDirectionUtility.ResolveDirectionFromWorld(incoming, transform);
            ReceiveHit(new HitContext(
                HitType.Heavy,
                dir,
                contactPoint,
                impulse: 2f,
                source: null,
                worldIncomingDirection: incoming));
        }

        public void DebugHitHeavy(Vector3 worldIncomingDirection, float impulseOverride, Vector3 contactPoint)
        {
            var incoming = worldIncomingDirection;
            incoming.y = 0f;
            if (incoming.sqrMagnitude < 0.0001f)
                incoming = transform.forward;
            incoming.Normalize();

            var dir = HitDirectionUtility.ResolveDirectionFromWorld(incoming, transform);
            ReceiveHit(new HitContext(
                HitType.Heavy,
                dir,
                contactPoint,
                impulse: Mathf.Max(0f, impulseOverride),
                source: null,
                worldIncomingDirection: incoming));
        }

        /// <summary>
        /// 调试：轻击级强制击倒（清空平衡值）
        /// Debug: force knockdown with light impulse semantics
        /// </summary>
        public void DebugForceKnockdownLight(Vector3 worldDirection, float impulseOverride) =>
            ReceiveHit(HitContext.DebugForceKnockdownLight(worldDirection, transform, impulseOverride));

        public void DebugForceKnockdownLight(Vector3 worldDirection, float impulseOverride, DebugHitContactRegion contactRegion) =>
            ReceiveHit(HitContext.DebugForceKnockdownLight(worldDirection, transform, impulseOverride, GetDebugContactPoint(contactRegion)));

        /// <summary>
        /// 调试：重击级强制击倒（清空平衡值）
        /// Debug: force knockdown with heavy impulse semantics
        /// </summary>
        public void DebugForceKnockdownHeavy(Vector3 worldDirection, float impulseOverride) =>
            ReceiveHit(HitContext.DebugForceKnockdownHeavy(worldDirection, transform, impulseOverride));

        public void DebugForceKnockdownHeavy(Vector3 worldDirection, float impulseOverride, DebugHitContactRegion contactRegion) =>
            ReceiveHit(HitContext.DebugForceKnockdownHeavy(worldDirection, transform, impulseOverride, GetDebugContactPoint(contactRegion)));

        /// <summary>
        /// 调试用接触点：优先 Humanoid 骨骼，缺失时回退到根节点偏移
        /// Debug contact point by humanoid bone; fallback to root offsets
        /// </summary>
        public Vector3 GetDebugContactPoint(DebugHitContactRegion region)
        {
            var modelAnimator = animator;
            if (modelAnimator != null && modelAnimator.isHuman && modelAnimator.avatar != null)
            {
                Transform bone = region switch
                {
                    DebugHitContactRegion.Hips => modelAnimator.GetBoneTransform(HumanBodyBones.Hips),
                    DebugHitContactRegion.Head => modelAnimator.GetBoneTransform(HumanBodyBones.Head),
                    DebugHitContactRegion.Chest => modelAnimator.GetBoneTransform(HumanBodyBones.UpperChest) ?? modelAnimator.GetBoneTransform(HumanBodyBones.Chest) ?? modelAnimator.GetBoneTransform(HumanBodyBones.Spine),
                    DebugHitContactRegion.LeftArm => modelAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm) ?? modelAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm),
                    DebugHitContactRegion.RightArm => modelAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm) ?? modelAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm),
                    DebugHitContactRegion.LeftLeg => modelAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg) ?? modelAnimator.GetBoneTransform(HumanBodyBones.LeftLowerLeg),
                    DebugHitContactRegion.RightLeg => modelAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg) ?? modelAnimator.GetBoneTransform(HumanBodyBones.RightLowerLeg),
                    _ => null
                };

                if (bone != null)
                    return bone.position;
            }

            var rootPos = transform.position;
            return region switch
            {
                DebugHitContactRegion.Hips => rootPos + Vector3.up * 0.95f,
                DebugHitContactRegion.Head => rootPos + Vector3.up * 1.7f,
                DebugHitContactRegion.Chest => rootPos + Vector3.up * 1.2f,
                DebugHitContactRegion.LeftArm => rootPos + Vector3.up * 1.2f - transform.right * 0.35f,
                DebugHitContactRegion.RightArm => rootPos + Vector3.up * 1.2f + transform.right * 0.35f,
                DebugHitContactRegion.LeftLeg => rootPos + Vector3.up * 0.65f - transform.right * 0.18f,
                DebugHitContactRegion.RightLeg => rootPos + Vector3.up * 0.65f + transform.right * 0.18f,
                _ => rootPos + Vector3.up
            };
        }

        #endregion
    }
}
