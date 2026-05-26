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

        [Header("Ragdoll / 布娃娃")]
        [Tooltip("Ragdoll 外观组件；留空则运行时自动使用本物体组件 — Ragdoll facade; uses component on this GameObject if null")]
        [SerializeField] CharacterRagdollSystem ragdollSystem;

        [Header("Attack Hit / 攻击命中")]
        [Tooltip("武器攻击命中驱动；留空则运行时自动查找子物体 — Weapon attack hit driver; auto-finds in children if null")]
        [SerializeField] WeaponAttackHitDriver attackHitDriver;

        [Header("Recovery / 起身")]
        [SerializeField] Transform spineTransform;
        [Tooltip("胸口朝向参考（建议 Spine2）；空则用 Spine — Torso facing for recoil; default Spine if null")]
        [SerializeField] Transform torsoFacingTransform;
        [Tooltip("颈部弯曲施加骨（可选）— Neck bone for procedural recoil")]
        [SerializeField] Transform neckRecoilTransform;
        [Tooltip("头部弯曲施加骨（建议 Head）— Head bone for procedural recoil")]
        [SerializeField] Transform headRecoilTransform;
        [Tooltip("进入 Recovering 前是否预先旋转到目标朝向；默认关闭以避免地面扭身感 — Pre-rotate before Recovering; off by default")]
        [SerializeField] bool preAlignRotationBeforeRecovering = false;
        [Tooltip("当前朝向与恢复锚点夹角超过该阈值时，强制在 Recovering 前对齐（度）— Force align before Recovering when angle exceeds threshold")]
        [SerializeField] float recoveryAxisForceAlignMinAngle = 20f;

        CharacterMotor _locomotion;
        CharacterAnimationPresenter _animation;
        CharacterCombat _combat;
        CharacterRecoveryFlow _recovery;
        CharacterRecoveryAlignment _recoveryAlignment;
        CharacterDebugHitDriver _debugHitDriver;

        readonly List<ICharacterModule> _modules = new();
        readonly CharacterContext _context = new();
        readonly CharacterStateMachine _hsm = new();

        bool _initialized;
        bool _lightFlinchOverlayActive;
        float _lightFlinchOverlayTime;
        bool _heavyStaggerAnimationFinished;

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
        public CharacterDebugHitDriver DebugHitDriver => _debugHitDriver;

        public bool HasLastDebugHit => _debugHitDriver != null && _debugHitDriver.HasLastDebugHit;
        public float LastDebugHitTime => _debugHitDriver != null ? _debugHitDriver.LastDebugHitTime : -1f;
        public HitContext LastDebugHit => _debugHitDriver != null ? _debugHitDriver.LastDebugHit : default;
        public Vector3 LastResolvedDebugContactPoint => _debugHitDriver != null ? _debugHitDriver.LastResolvedDebugContactPoint : transform.position;
        public string LastResolvedDebugContactSource => _debugHitDriver != null ? _debugHitDriver.LastResolvedDebugContactSource : "Unknown";
        public string LastResolvedDebugContactBoneName => _debugHitDriver != null ? _debugHitDriver.LastResolvedDebugContactBoneName : string.Empty;
        public bool IsInKnockdownPhase =>
            _hsm.CurrentState is CharacterState.Knockdown or CharacterState.ForcedKnockdown;
        public bool IsInRecoveryPhase => _hsm.CurrentState == CharacterState.Recovering;
        public bool IsWeaponEquipped => _animation != null && _animation.IsWeaponEquipped;

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

        /// <summary>
        /// 是否允许消费玩法根运动（重击/攻击，且对应配置开启）
        /// Whether gameplay root-motion consumption is currently allowed (heavy hit / attack)
        /// </summary>
        public bool AllowsGameplayRootMotion =>
            _initialized &&
            config != null &&
            ((_hsm.CurrentState == CharacterState.HeavyStagger && config.heavyStaggerUseRootMotion)
                || (_hsm.CurrentState == CharacterState.AttackPlayback && config.attackUseRootMotion));

        public bool AllowsHeavyRootMotion => AllowsGameplayRootMotion;

        void Awake()
        {
            InitializeModules();
        }

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
            if (attackHitDriver == null)
                attackHitDriver = GetComponentInChildren<WeaponAttackHitDriver>(true);

            ragdollSystem.InitializeRuntime(
                transform,
                animator != null ? animator.transform : transform);
            _recovery = new CharacterRecoveryFlow();
            _recoveryAlignment = new CharacterRecoveryAlignment(
                transform,
                animator,
                unityCharacterController,
                animationConfig,
                preAlignRotationBeforeRecovering,
                recoveryAxisForceAlignMinAngle);
            _debugHitDriver = new CharacterDebugHitDriver(this, animator, ragdollSystem);

            _modules.Clear();
            _modules.Add(_locomotion);
            _modules.Add(_animation);
            _modules.Add(_combat);
            _modules.Add(ragdollSystem);
            _modules.Add(_recovery);

            _hsm.ResetToLocomotion();

            foreach (var module in _modules)
                module.OnEnterState(CharacterState.Locomotion, default);

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
            {
                // 冻结输入期间强制归零动画机速度参数，避免残留跑步状态
                // Force-zero locomotion animator params during input-locked states to prevent run-state residue
                _animation.SyncLocomotionAnimator(0f);
                return;
            }

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

        public bool TryLightAttack() => TryBeginAttack(CharacterAttackType.Light);

        public bool TryHeavyAttack() => TryBeginAttack(CharacterAttackType.Heavy);

        bool TryBeginAttack(CharacterAttackType attackType)
        {
            if (!_initialized || _animation == null)
                return false;
            if (_hsm.CurrentState != CharacterState.Locomotion)
                return false;
            if (!_animation.IsWeaponEquipped || _animation.IsAttackPlaybackInProgress)
                return false;
            if (!_animation.TryBeginAttack(attackType))
                return false;

            if (TransitionTo(CharacterState.AttackPlayback, default, recordHit: false))
                return true;

            _animation.AbortAttackPlayback();
            return false;
        }

        void TickStateTransitions()
        {
            switch (_hsm.CurrentState)
            {
                case CharacterState.WeaponEquipPlayback:
                    if (_animation != null && !_animation.IsEquipPlaybackInProgress)
                        TransitionTo(CharacterState.Locomotion, default, recordHit: false);
                    break;

                case CharacterState.AttackPlayback:
                    if (_animation != null && !_animation.IsAttackPlaybackInProgress)
                        TransitionTo(CharacterState.Locomotion, default, recordHit: false);
                    break;

                case CharacterState.LightFlinch:
                    if (config != null && _hsm.TimeInState >= config.lightFlinchDuration)
                        TransitionTo(CharacterState.Locomotion, default, recordHit: false);
                    break;

                case CharacterState.HeavyStagger:
                    if (_heavyStaggerAnimationFinished &&
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
                            : RecoveryGetUpType.Back;

                        _recoveryAlignment?.AlignForRecovery(
                            ref recoveryAnchor,
                            getUpType,
                            out var resolvedForward,
                            out var shouldFlipBack,
                            out var angleToAnchor,
                            out var appliedVisualRotation);

                        _recovery?.BeginRecovery(getUpType, _recoveryAlignment?.ResolveRecoveryFallbackDuration(getUpType) ?? 2f);
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

            var resolvedHitContext = hitContext;

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
                resolvedHitContext = ResolveHitDirectionAfterBalance(in hitContext, depleted);
                target = CharacterStateMachine.ResolveHitTarget(hitContext, depleted);
            }

            _debugHitDriver?.RecordDebugHit(in resolvedHitContext);
            _context.LastHit = resolvedHitContext;

            if (target == CharacterState.HeavyStagger)
                ApplyHeavyHitFacing(in resolvedHitContext);

            if (TryApplyGroundedLightHitOverlay(in resolvedHitContext, target))
                return;

            EndLightFlinchOverlay();

            if (_hsm.ShouldAbortWeaponOnTransition(target))
                _animation?.AbortWeaponEquipPlayback();

            TransitionTo(target, resolvedHitContext);
        }

        HitContext ResolveHitDirectionAfterBalance(in HitContext hitContext, bool balanceDepleted)
        {
            if (balanceDepleted || !hitContext.HasWorldSource)
                return hitContext;

            var sourceDirection = hitContext.WorldSourceDirection;
            sourceDirection.y = 0f;
            if (sourceDirection.sqrMagnitude < 0.0001f)
                return hitContext;
            sourceDirection.Normalize();

            var direction = HitDirectionUtility.ResolveDirectionFromWorld(sourceDirection, transform);
            return new HitContext(
                hitContext.Type,
                direction,
                hitContext.ContactPoint,
                hitContext.Impulse,
                hitContext.BypassBalance,
                hitContext.Source,
                sourceDirection,
                hitContext.WorldSourceDirection);
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

            if (newState == CharacterState.HeavyStagger)
                _heavyStaggerAnimationFinished = false;
            else if (oldState == CharacterState.HeavyStagger)
                _heavyStaggerAnimationFinished = false;

            if (oldState == CharacterState.AttackPlayback)
                attackHitDriver?.EndAttackHitWindow();

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
            _context.IsRagdollSettled = IsRagdollSettled;
            _context.RagdollMode = CurrentRagdollMode;
            _context.RagdollBackendStatus = RagdollBackendStatus;
            _context.RagdollChainName = CurrentRagdollChain;
            _context.RagdollMappedBoneCount = RagdollMappedBoneCount;
            _context.IsUsingDualRagdoll = IsUsingDualRagdoll;
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

        /// <summary>
        /// 消费 Animator 提取的玩法根运动位移（仅 XZ），并回写到 Player 根节点
        /// Consume animator gameplay root-motion delta (planar only) and apply to player root
        /// </summary>
        public void ConsumeGameplayRootMotion(Vector3 animatorDeltaPosition)
        {
            if (!AllowsGameplayRootMotion)
                return;
            if (unityCharacterController == null || !unityCharacterController.enabled)
                return;

            var planarDelta = animatorDeltaPosition;
            planarDelta.y = 0f;
            if (planarDelta.sqrMagnitude < 0.000001f)
                return;

            var scale = Mathf.Max(0f, ResolveGameplayRootMotionPlanarScale());
            planarDelta *= scale;

            var maxPerFrame = Mathf.Max(0f, ResolveGameplayRootMotionMaxDeltaPerFrame());
            if (maxPerFrame > 0f)
                planarDelta = Vector3.ClampMagnitude(planarDelta, maxPerFrame);

            unityCharacterController.Move(planarDelta);
        }

        public void ConsumeHeavyStaggerRootMotion(Vector3 animatorDeltaPosition) =>
            ConsumeGameplayRootMotion(animatorDeltaPosition);

        float ResolveGameplayRootMotionPlanarScale()
        {
            if (config == null)
                return 0f;
            return _hsm.CurrentState == CharacterState.AttackPlayback
                ? config.attackRootMotionPlanarScale
                : config.heavyRootMotionPlanarScale;
        }

        float ResolveGameplayRootMotionMaxDeltaPerFrame()
        {
            if (config == null)
                return 0f;
            return _hsm.CurrentState == CharacterState.AttackPlayback
                ? config.attackRootMotionMaxDeltaPerFrame
                : config.heavyRootMotionMaxDeltaPerFrame;
        }

        void ApplyHeavyHitFacing(in HitContext hitContext)
        {
            var incoming = HitDirectionUtility.ResolveIncomingWorld(in hitContext, transform);
            incoming.y = 0f;
            if (incoming.sqrMagnitude < 0.0001f)
                return;

            // incoming 表示攻击源指向角色的施力方向；角色受重击后应面向攻击源
            // incoming is the force direction from attack source to character; after heavy hit, face back toward the source
            var forward = -incoming.normalized;
            var targetRotation = Quaternion.LookRotation(forward, Vector3.up);
            var visualRoot = animator != null ? animator.transform : transform;
            visualRoot.rotation = targetRotation;
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

        /// <summary>
        /// 重击动画末帧事件：允许退出 HeavyStagger 并恢复输入
        /// Heavy-stagger clip end event: allow leaving HeavyStagger and restoring input
        /// </summary>
        public void NotifyHeavyStaggerFinished()
        {
            if (!_initialized)
                return;
            if (_hsm.CurrentState != CharacterState.HeavyStagger)
                return;

            _heavyStaggerAnimationFinished = true;
        }

        /// <summary>
        /// 攻击动画末帧事件：允许攻击层淡出并恢复输入
        /// Attack clip end event: allow attack layer fade-out and input restore
        /// </summary>
        public void NotifyAttackFinished()
        {
            if (!_initialized)
                return;
            if (_hsm.CurrentState != CharacterState.AttackPlayback)
                return;

            attackHitDriver?.EndAttackHitWindow();
            _animation?.CompleteAttackPlayback();
        }

        public void NotifyLightAttackHitStart()
        {
            if (!_initialized || _hsm.CurrentState != CharacterState.AttackPlayback)
                return;

            attackHitDriver?.BeginLightAttackHitWindow();
        }

        public void NotifyHeavyAttackHitStart()
        {
            if (!_initialized || _hsm.CurrentState != CharacterState.AttackPlayback)
                return;

            attackHitDriver?.BeginHeavyAttackHitWindow();
        }

        public void NotifyAttackHitEnd()
        {
            if (!_initialized)
                return;

            attackHitDriver?.EndAttackHitWindow();
        }

        #endregion
    }
}
