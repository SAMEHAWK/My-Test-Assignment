using UnityEngine;
using System.Collections.Generic;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 运行时调试受击与状态（挂于 Player 根节点）
    /// Runtime hit/state debug on player root
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterControllerDebug : MonoBehaviour
    {
        [SerializeField] CharacterControllerRoot root;
        [SerializeField] HitDirection debugDirection = HitDirection.Front;
        [Header("360 Direction Debug / 360方向调试")]
        [SerializeField] bool useContinuousDirection = true;
        [SerializeField] float debugYawDegrees;
        [SerializeField] bool invertIncoming;
        [SerializeField] HitDirectionDebugHelper directionHelper;
        [Header("Contact Point / 接触点")]
        [SerializeField] DebugHitContactRegion debugContactRegion = DebugHitContactRegion.Chest;
        [SerializeField] KeyCode headKey = KeyCode.F1;
        [SerializeField] KeyCode chestKey = KeyCode.F2;
        [SerializeField] KeyCode hipsKey = KeyCode.F3;
        [SerializeField] KeyCode leftArmKey = KeyCode.F4;
        [SerializeField] KeyCode rightArmKey = KeyCode.F5;
        [SerializeField] KeyCode leftLegKey = KeyCode.F6;
        [SerializeField] KeyCode rightLegKey = KeyCode.F7;
        [SerializeField] KeyCode leftElbowKey = KeyCode.F8;
        [SerializeField] KeyCode rightElbowKey = KeyCode.F9;
        [SerializeField] KeyCode leftWristKey = KeyCode.F10;
        [SerializeField] KeyCode rightWristKey = KeyCode.F11;
        [SerializeField] KeyCode leftKneeKey = KeyCode.F12;
        [SerializeField] KeyCode rightKneeKey = KeyCode.F13;
        [SerializeField] KeyCode leftAnkleKey = KeyCode.F14;
        [SerializeField] KeyCode rightAnkleKey = KeyCode.F15;
        [Header("Force KO Debug / 强制击倒调试冲量")]
        [SerializeField] float debugHeavyHitImpulse = 6f;
        [SerializeField] float debugForceKoLightImpulse = 2f;
        [SerializeField] float debugForceKoHeavyImpulse = 6f;
        [Header("Gizmos / 可视化")]
        [SerializeField] bool showHitGizmos = true;
        [SerializeField] bool showAffectedBones = true;
        [SerializeField] bool drawOnlyWhenSelected;
        [SerializeField] float gizmoDuration = 2f;
        [SerializeField] float impulseArrowLength = 0.8f;
        [SerializeField] float pointSphereRadius = 0.06f;
        [SerializeField] float affectedSphereRadius = 0.04f;

        bool _hasActiveHitGizmo;
        bool _hasObservedHitGizmoSource;
        bool _activeHitGizmoUsesRagdoll;
        float _activeHitGizmoSourceTime = -1f;
        float _hitGizmoRemainingTime;
        int _lastHitGizmoTickFrame = -1;

        void Reset()
        {
            root = GetComponent<CharacterControllerRoot>();
            AutoBindDirectionHelper();
        }

        void Awake() => AutoBindDirectionHelper();

        void OnValidate() => AutoBindDirectionHelper();

        void Update()
        {
            TickContactRegionHotkeys();
            TickHitGizmoLifetimeOncePerFrame();
        }

        void OnDrawGizmos()
        {
            if (drawOnlyWhenSelected)
                return;
            DrawHitGizmos();
        }

        void OnDrawGizmosSelected()
        {
            DrawHitGizmos();
        }

        void AutoBindDirectionHelper()
        {
            if (directionHelper != null)
                return;

            directionHelper = FindFirstObjectByType<HitDirectionDebugHelper>();
        }

        void OnGUI()
        {
            if (root == null)
                return;

            const int width = 340;
            const int line = 24;
            var x = 12;
            var y = 12;

            GUI.Box(new Rect(x - 4, y - 4, width + 8, line * 56 + 12), "Character Debug");
            y += line;

            var ctx = root.Context;
            GUI.Label(new Rect(x, y, width, line), "当前状态 / Current state");
            y += line;
            GUI.Label(new Rect(x, y, width, line), $"State: {ctx.State} ({ctx.Superstate})");
            y += line;
            GUI.Label(new Rect(x, y, width, line), "状态时长与可否拔刀 / State time and weapon toggle");
            y += line;
            GUI.Label(new Rect(x, y, width, line), $"t={ctx.StateTime:F2}  E={ctx.CanToggleWeapon}");
            y += line;
            if (ctx.IsLightFlinchOverlayActive)
            {
                GUI.Label(new Rect(x, y, width, line), "Flinch overlay");
                y += line;
            }
            GUI.Label(new Rect(x, y, width, line), "平衡值（归零会击倒）/ Balance (0 triggers knockdown)");
            y += line;
            GUI.Label(new Rect(x, y, width, line), $"Balance: {ctx.CurrentBalance}/{ctx.MaxBalance}");
            y += line;
            GUI.Label(new Rect(x, y, width, line), "布娃娃是否沉降完成 / Ragdoll settle flag");
            y += line;
            GUI.Label(new Rect(x, y, width, line), $"RagdollSettled: {ctx.IsRagdollSettled}");
            y += line;
            GUI.Label(new Rect(x, y, width, line), $"RagdollMode: {ctx.RagdollMode}");
            y += line;
            GUI.Label(new Rect(x, y, width, line), $"RagdollBackend: {(ctx.IsUsingDualRagdoll ? "Dual" : "Unavailable")}");
            y += line;
            GUI.Label(new Rect(x, y, width, line), $"RagdollBackendStatus: {ctx.RagdollBackendStatus}");
            y += line;
            GUI.Label(new Rect(x, y, width, line), $"RagdollChain: {ctx.RagdollChainName}  Mapped: {ctx.RagdollMappedBoneCount}");
            y += line;
            GUI.Label(new Rect(x, y, width, line), "当前受击接触点 / Current contact region");
            y += line;
            GUI.Label(new Rect(x, y, width, line), $"Contact: {debugContactRegion}");
            y += line;
            GUI.Label(new Rect(x, y, width, line), $"ContactSource: {root.LastResolvedDebugContactSource}");
            y += line;
            GUI.Label(new Rect(x, y, width, line), $"ContactBone: {(string.IsNullOrEmpty(root.LastResolvedDebugContactBoneName) ? "<none>" : root.LastResolvedDebugContactBoneName)}");
            y += line;
            var resolvedContact = root.LastResolvedDebugContactPoint;
            GUI.Label(new Rect(x, y, width, line), $"ContactPoint: ({resolvedContact.x:F2},{resolvedContact.y:F2},{resolvedContact.z:F2})");
            y += line;

            GUI.Label(new Rect(x, y, width, line), "开关：是否使用 360° 连续方向 / Use continuous direction");
            y += line;
            useContinuousDirection = GUI.Toggle(new Rect(x, y, width, line), useContinuousDirection, "Use 360 incoming");
            y += line;

            if (useContinuousDirection)
            {
                GUI.Label(new Rect(x, y, width, line), "Yaw 角度（0~360）/ Incoming yaw");
                y += line;
                GUI.Label(new Rect(x, y, width, line), $"Yaw: {debugYawDegrees:F1}");
                y += line;

                debugYawDegrees = GUI.HorizontalSlider(new Rect(x, y + 6, width, line), debugYawDegrees, 0f, 360f);
                debugYawDegrees = Mathf.Repeat(debugYawDegrees, 360f);
                y += line;

                GUI.Label(new Rect(x, y, width, line), "快捷方向（前后左右）/ Quick direction presets");
                y += line;
                var fblrIndex = GUI.Toolbar(
                    new Rect(x, y, width, line),
                    -1,
                    new[] { "F", "B", "L", "R" });
                if (fblrIndex >= 0)
                    ApplyShortcutYaw(fblrIndex);
                y += line;

                GUI.Label(new Rect(x, y, width, line), "反转来源语义 / Invert source bearing");
                y += line;
                invertIncoming = GUI.Toggle(new Rect(x, y, width, line), invertIncoming, "Invert source bearing");
                y += line;
            }
            else
            {
                GUI.Label(new Rect(x, y, width, line), "四向离散方向 / 4-way discrete direction");
                y += line;
                debugDirection = (HitDirection)GUI.Toolbar(
                    new Rect(x, y, width, line),
                    (int)debugDirection,
                    new[] { "F", "B", "L", "R" });
                y += line;
            }

            GUI.Label(new Rect(x, y, width, line), "当前受力方向向量（XZ）/ Current force direction vector");
            y += line;
            var debugDirectionWorld = BuildWorldIncomingDirection();
            GUI.Label(new Rect(x, y, width, line), $"Dir(xz)=({debugDirectionWorld.x:F2},{debugDirectionWorld.z:F2})");
            y += line;

            GUI.Label(new Rect(x, y, width, line), "接触点部位选择 / Contact region picker");
            y += line;
            var topRow = GUI.Toolbar(
                new Rect(x, y, width, line),
                GetContactToolbarTopIndex(debugContactRegion),
                new[] { "Head", "Chest", "Hips", "Left Arm", "Right Arm" });
            if (topRow >= 0)
                ApplyTopContactToolbarSelection(topRow, ref debugContactRegion);
            y += line;

            var armDetailRow = GUI.Toolbar(
                new Rect(x, y, width, line),
                GetArmDetailToolbarIndex(debugContactRegion),
                new[] { "Left Elbow", "Right Elbow", "Left Wrist", "Right Wrist" });
            if (armDetailRow >= 0)
                ApplyArmDetailToolbarSelection(armDetailRow, ref debugContactRegion);
            y += line;

            var legRow = GUI.Toolbar(
                new Rect(x, y, width, line),
                GetLegToolbarIndex(debugContactRegion),
                new[] { "Left Leg", "Right Leg", "Left Knee", "Right Knee" });
            if (legRow >= 0)
                ApplyLegToolbarSelection(legRow, ref debugContactRegion);
            y += line;

            var ankleRow = GUI.Toolbar(
                new Rect(x, y, width, line),
                GetAnkleToolbarIndex(debugContactRegion),
                new[] { "Left Ankle", "Right Ankle" });
            if (ankleRow >= 0)
                ApplyAnkleToolbarSelection(ankleRow, ref debugContactRegion);
            y += line;

            GUI.Label(new Rect(x, y, width, line), "接触点快捷键 / Contact region hotkeys");
            y += line;
            GUI.Label(new Rect(x, y, width, line), $"Base: {headKey}/{chestKey}/{hipsKey}/{leftArmKey}/{rightArmKey}/{leftLegKey}/{rightLegKey}");
            y += line;
            GUI.Label(new Rect(x, y, width, line), $"Detail: {leftElbowKey}/{rightElbowKey}/{leftWristKey}/{rightWristKey}/{leftKneeKey}/{rightKneeKey}/{leftAnkleKey}/{rightAnkleKey}");
            y += line;

            GUI.Label(new Rect(x, y, width, line), "轻击调试按钮（不击倒）/ Light hit trigger");
            y += line;
            if (GUI.Button(new Rect(x, y, width, line), "Light Hit"))
            {
                if (useContinuousDirection)
                {
                    // 需求语义：箭头表示受力方向；轻击逻辑内部按“来向取反”驱动反应，因此此处传反向以得到“朝力方向”效果
                    // Required semantics: arrow visualizes force direction; light-hit response internally negates incoming, so pass inverse here to move toward force
                    root.DebugHitLight(-debugDirectionWorld, root.GetDebugContactPoint(debugContactRegion));
                }
                else
                    root.DebugHitLight(debugDirection, debugContactRegion);
            }
            y += line;

            GUI.Label(new Rect(x, y, width, line), "重击调试按钮（可能击倒）/ Heavy hit trigger");
            y += line;
            GUI.Label(new Rect(x, y, width, line), $"Heavy Impulse: {debugHeavyHitImpulse:F2}");
            y += line;
            debugHeavyHitImpulse = GUI.HorizontalSlider(
                new Rect(x, y + 6, width, line),
                debugHeavyHitImpulse,
                0f,
                20f);
            y += line;
            if (GUI.Button(new Rect(x, y, width, line), "Heavy Hit"))
            {
                if (useContinuousDirection)
                    root.DebugHitHeavy(debugDirectionWorld, debugHeavyHitImpulse, root.GetDebugContactPoint(debugContactRegion));
                else
                    root.DebugHitHeavy(debugDirection, debugHeavyHitImpulse, debugContactRegion);
            }
            y += line;

            GUI.Label(new Rect(x, y, width, line), "强制轻击倒冲量 / Force KO Light impulse");
            y += line;
            GUI.Label(new Rect(x, y, width, line), $"ForceKO Light Impulse: {debugForceKoLightImpulse:F2}");
            y += line;
            debugForceKoLightImpulse = GUI.HorizontalSlider(
                new Rect(x, y + 6, width, line),
                debugForceKoLightImpulse,
                0f,
                20f);
            y += line;

            GUI.Label(new Rect(x, y, width, line), "强制重击倒冲量 / Force KO Heavy impulse");
            y += line;
            GUI.Label(new Rect(x, y, width, line), $"ForceKO Heavy Impulse: {debugForceKoHeavyImpulse:F2}");
            y += line;
            debugForceKoHeavyImpulse = GUI.HorizontalSlider(
                new Rect(x, y + 6, width, line),
                debugForceKoHeavyImpulse,
                0f,
                30f);
            y += line;

            GUI.Label(new Rect(x, y, width, line), "强制轻击倒（清空平衡值）/ Force KO Light");
            y += line;
            if (GUI.Button(new Rect(x, y, width, line), "Force KO Light"))
            {
                if (useContinuousDirection)
                    root.DebugForceKnockdownLight(debugDirectionWorld, debugForceKoLightImpulse, debugContactRegion);
                else
                    root.DebugForceKnockdownLight(-HitDirectionUtility.ToWorldVector(debugDirection, root.transform), debugForceKoLightImpulse, debugContactRegion);
            }
            y += line;

            GUI.Label(new Rect(x, y, width, line), "强制重击倒（清空平衡值）/ Force KO Heavy");
            y += line;
            if (GUI.Button(new Rect(x, y, width, line), "Force KO Heavy"))
            {
                if (useContinuousDirection)
                    root.DebugForceKnockdownHeavy(debugDirectionWorld, debugForceKoHeavyImpulse, debugContactRegion);
                else
                    root.DebugForceKnockdownHeavy(-HitDirectionUtility.ToWorldVector(debugDirection, root.transform), debugForceKoHeavyImpulse, debugContactRegion);
            }

            SyncHelperFromPanel();
        }

        void ApplyShortcutYaw(int shortcutIndex)
        {
            switch (shortcutIndex)
            {
                case 0: // F
                    debugYawDegrees = 0f;
                    break;
                case 1: // B
                    debugYawDegrees = 180f;
                    break;
                case 2: // L
                    debugYawDegrees = 270f;
                    break;
                case 3: // R
                    debugYawDegrees = 90f;
                    break;
            }

            debugYawDegrees = Mathf.Repeat(debugYawDegrees, 360f);
        }

        void SyncHelperFromPanel()
        {
            if (directionHelper == null)
                return;

            if (root != null)
                directionHelper.SetTargetRoot(root);
            directionHelper.SetYawDegrees(debugYawDegrees);
            directionHelper.SetInvertIncoming(invertIncoming);
        }

        Vector3 BuildWorldIncomingDirection()
        {
            var sourceBearing = Quaternion.Euler(0f, debugYawDegrees, 0f) * Vector3.forward;
            if (invertIncoming)
                sourceBearing = -sourceBearing;

            sourceBearing.y = 0f;
            if (sourceBearing.sqrMagnitude < 0.0001f)
                sourceBearing = Vector3.forward;

            // 统一语义：incoming = 来源 -> 角色
            // Unified semantics: incoming = source -> character
            return -sourceBearing.normalized;
        }

        void TickContactRegionHotkeys()
        {
            if (Input.GetKeyDown(headKey))
                debugContactRegion = DebugHitContactRegion.Head;
            else if (Input.GetKeyDown(chestKey))
                debugContactRegion = DebugHitContactRegion.Chest;
            else if (Input.GetKeyDown(hipsKey))
                debugContactRegion = DebugHitContactRegion.Hips;
            else if (Input.GetKeyDown(leftArmKey))
                debugContactRegion = DebugHitContactRegion.LeftArm;
            else if (Input.GetKeyDown(rightArmKey))
                debugContactRegion = DebugHitContactRegion.RightArm;
            else if (Input.GetKeyDown(leftLegKey))
                debugContactRegion = DebugHitContactRegion.LeftLeg;
            else if (Input.GetKeyDown(rightLegKey))
                debugContactRegion = DebugHitContactRegion.RightLeg;
            else if (Input.GetKeyDown(leftElbowKey))
                debugContactRegion = DebugHitContactRegion.LeftElbow;
            else if (Input.GetKeyDown(rightElbowKey))
                debugContactRegion = DebugHitContactRegion.RightElbow;
            else if (Input.GetKeyDown(leftWristKey))
                debugContactRegion = DebugHitContactRegion.LeftWrist;
            else if (Input.GetKeyDown(rightWristKey))
                debugContactRegion = DebugHitContactRegion.RightWrist;
            else if (Input.GetKeyDown(leftKneeKey))
                debugContactRegion = DebugHitContactRegion.LeftKnee;
            else if (Input.GetKeyDown(rightKneeKey))
                debugContactRegion = DebugHitContactRegion.RightKnee;
            else if (Input.GetKeyDown(leftAnkleKey))
                debugContactRegion = DebugHitContactRegion.LeftAnkle;
            else if (Input.GetKeyDown(rightAnkleKey))
                debugContactRegion = DebugHitContactRegion.RightAnkle;
        }

        static int GetContactToolbarTopIndex(DebugHitContactRegion region) =>
            region switch
            {
                DebugHitContactRegion.Head => 0,
                DebugHitContactRegion.Chest => 1,
                DebugHitContactRegion.Hips => 2,
                DebugHitContactRegion.LeftArm => 3,
                DebugHitContactRegion.RightArm => 4,
                _ => -1
            };

        static void ApplyTopContactToolbarSelection(int index, ref DebugHitContactRegion region)
        {
            region = index switch
            {
                0 => DebugHitContactRegion.Head,
                1 => DebugHitContactRegion.Chest,
                2 => DebugHitContactRegion.Hips,
                3 => DebugHitContactRegion.LeftArm,
                4 => DebugHitContactRegion.RightArm,
                _ => region
            };
        }

        static int GetArmDetailToolbarIndex(DebugHitContactRegion region) =>
            region switch
            {
                DebugHitContactRegion.LeftElbow => 0,
                DebugHitContactRegion.RightElbow => 1,
                DebugHitContactRegion.LeftWrist => 2,
                DebugHitContactRegion.RightWrist => 3,
                _ => -1
            };

        static void ApplyArmDetailToolbarSelection(int index, ref DebugHitContactRegion region)
        {
            region = index switch
            {
                0 => DebugHitContactRegion.LeftElbow,
                1 => DebugHitContactRegion.RightElbow,
                2 => DebugHitContactRegion.LeftWrist,
                3 => DebugHitContactRegion.RightWrist,
                _ => region
            };
        }

        static int GetLegToolbarIndex(DebugHitContactRegion region) =>
            region switch
            {
                DebugHitContactRegion.LeftLeg => 0,
                DebugHitContactRegion.RightLeg => 1,
                DebugHitContactRegion.LeftKnee => 2,
                DebugHitContactRegion.RightKnee => 3,
                _ => -1
            };

        static void ApplyLegToolbarSelection(int index, ref DebugHitContactRegion region)
        {
            region = index switch
            {
                0 => DebugHitContactRegion.LeftLeg,
                1 => DebugHitContactRegion.RightLeg,
                2 => DebugHitContactRegion.LeftKnee,
                3 => DebugHitContactRegion.RightKnee,
                _ => region
            };
        }

        static int GetAnkleToolbarIndex(DebugHitContactRegion region) =>
            region switch
            {
                DebugHitContactRegion.LeftAnkle => 0,
                DebugHitContactRegion.RightAnkle => 1,
                _ => -1
            };

        static void ApplyAnkleToolbarSelection(int index, ref DebugHitContactRegion region)
        {
            region = index switch
            {
                0 => DebugHitContactRegion.LeftAnkle,
                1 => DebugHitContactRegion.RightAnkle,
                _ => region
            };
        }

        void TickHitGizmoLifetimeOncePerFrame()
        {
            if (!Application.isPlaying)
                return;
            if (_lastHitGizmoTickFrame == Time.frameCount)
                return;

            _lastHitGizmoTickFrame = Time.frameCount;
            TickHitGizmoLifetime(Time.deltaTime);
        }

        void TickHitGizmoLifetime(float deltaTime)
        {
            if (root == null)
                root = GetComponent<CharacterControllerRoot>();
            if (root == null)
            {
                _hasActiveHitGizmo = false;
                return;
            }

            if (!TryResolveHitGizmoSource(root, out var useRagdoll, out var sourceTime))
            {
                _hasActiveHitGizmo = false;
                return;
            }

            var isNewSource = !_hasObservedHitGizmoSource
                || _activeHitGizmoUsesRagdoll != useRagdoll
                || !Mathf.Approximately(_activeHitGizmoSourceTime, sourceTime);
            if (isNewSource)
            {
                _hasObservedHitGizmoSource = true;
                _hasActiveHitGizmo = true;
                _activeHitGizmoUsesRagdoll = useRagdoll;
                _activeHitGizmoSourceTime = sourceTime;
                _hitGizmoRemainingTime = gizmoDuration;
            }

            if (gizmoDuration <= 0f)
                return;

            // Gizmo 生命周期按游戏时间递减；逐帧保护避免 OnDrawGizmos 多次重绘导致提前消失
            // Gizmo lifetime is decremented with game time; per-frame guard prevents repeated Gizmo repaints from expiring it early
            _hitGizmoRemainingTime -= Mathf.Max(0f, deltaTime);
            if (_hitGizmoRemainingTime <= 0f)
                _hasActiveHitGizmo = false;
        }

        static bool TryResolveHitGizmoSource(CharacterControllerRoot targetRoot, out bool useRagdoll, out float sourceTime)
        {
            useRagdoll = false;
            sourceTime = -1f;
            if (targetRoot == null)
                return false;

            var hasRagdollDebug = targetRoot.HasRagdollDebugHitInfo;
            var hasRootFallback = targetRoot.HasLastDebugHit;
            if (!hasRagdollDebug && !hasRootFallback)
                return false;

            var ragdollTime = hasRagdollDebug ? targetRoot.RagdollDebugLastHitTime : -1f;
            var rootTime = hasRootFallback ? targetRoot.LastDebugHitTime : -1f;
            useRagdoll = hasRagdollDebug && (!hasRootFallback || ragdollTime >= rootTime);
            sourceTime = useRagdoll ? ragdollTime : rootTime;
            return sourceTime >= 0f;
        }

        void DrawHitGizmos()
        {
            if (!showHitGizmos)
                return;

            if (root == null)
                root = GetComponent<CharacterControllerRoot>();
            if (root == null)
                return;

            TickHitGizmoLifetimeOncePerFrame();

            if (!TryResolveHitGizmoSource(root, out var hasRagdollDebug, out _))
                return;

            if (Application.isPlaying && gizmoDuration > 0f && !_hasActiveHitGizmo)
                return;

            var fallbackHit = root.LastDebugHit;
            var hitPoint = hasRagdollDebug ? root.RagdollDebugImpulsePoint : fallbackHit.ContactPoint;
            var impulseDir = hasRagdollDebug
                ? root.RagdollDebugImpulseDirection
                : HitDirectionUtility.ResolveIncomingWorld(in fallbackHit, root.transform);
            if (impulseDir.sqrMagnitude < 0.0001f)
                impulseDir = transform.forward;

            Gizmos.color = Color.red;
            Gizmos.DrawSphere(hitPoint, pointSphereRadius);

            DrawArrow(hitPoint, impulseDir.normalized * impulseArrowLength, Color.yellow);

            var primaryBone = hasRagdollDebug ? root.RagdollDebugPrimaryAffectedBone : null;
            if (primaryBone != null)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawSphere(primaryBone.position, pointSphereRadius * 0.9f);
                Gizmos.DrawLine(hitPoint, primaryBone.position);
            }

            if (!showAffectedBones)
                return;

            IReadOnlyList<Transform> affectedBones = hasRagdollDebug ? root.RagdollDebugAffectedBones : null;
            if (affectedBones == null)
                return;

            Gizmos.color = Color.green;
            for (var i = 0; i < affectedBones.Count; i++)
            {
                var bone = affectedBones[i];
                if (bone == null)
                    continue;
                if (primaryBone != null && bone == primaryBone)
                    continue;
                Gizmos.DrawSphere(bone.position, affectedSphereRadius);
                Gizmos.DrawLine(hitPoint, bone.position);
            }
        }

        static void DrawArrow(Vector3 origin, Vector3 vector, Color color)
        {
            Gizmos.color = color;
            var end = origin + vector;
            Gizmos.DrawLine(origin, end);

            var length = vector.magnitude;
            if (length < 0.001f)
                return;

            var dir = vector / length;
            var side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 0.0001f)
                side = Vector3.Cross(dir, Vector3.right);
            side.Normalize();
            var up = Vector3.Cross(side, dir).normalized;

            var headLength = Mathf.Max(0.08f, length * 0.2f);
            var headWidth = headLength * 0.35f;
            var headBase = end - dir * headLength;

            Gizmos.DrawLine(end, headBase + side * headWidth);
            Gizmos.DrawLine(end, headBase - side * headWidth);
            Gizmos.DrawLine(end, headBase + up * headWidth);
            Gizmos.DrawLine(end, headBase - up * headWidth);
        }
    }
}
