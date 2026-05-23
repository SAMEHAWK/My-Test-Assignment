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

        void Reset()
        {
            root = GetComponent<CharacterControllerRoot>();
            AutoBindDirectionHelper();
        }

        void Awake() => AutoBindDirectionHelper();

        void OnValidate() => AutoBindDirectionHelper();

        void Update() => TickContactRegionHotkeys();

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

            GUI.Box(new Rect(x - 4, y - 4, width + 8, line * 44 + 12), "Character Debug");
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
            GUI.Label(new Rect(x, y, width, line), $"RagdollSettled: {root.IsRagdollSettled}");
            y += line;
            GUI.Label(new Rect(x, y, width, line), $"RagdollMode: {root.CurrentRagdollMode}");
            y += line;
            GUI.Label(new Rect(x, y, width, line), $"RagdollBackend: {(root.IsUsingDualRagdoll ? "Dual" : root.IsUsingLegacyRagdollFallback ? "LegacyFallback" : "Uninitialized")}");
            y += line;
            GUI.Label(new Rect(x, y, width, line), $"RagdollBackendStatus: {root.RagdollBackendStatus}");
            y += line;
            GUI.Label(new Rect(x, y, width, line), $"RagdollChain: {root.CurrentRagdollChain}  Mapped: {root.RagdollMappedBoneCount}");
            y += line;
            GUI.Label(new Rect(x, y, width, line), "当前受击接触点 / Current contact region");
            y += line;
            GUI.Label(new Rect(x, y, width, line), $"Contact: {debugContactRegion}");
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

            GUI.Label(new Rect(x, y, width, line), "当前方向向量（XZ）/ Current direction vector");
            y += line;
            var debugDirectionWorld = BuildWorldIncomingDirection();
            GUI.Label(new Rect(x, y, width, line), $"Dir(xz)=({debugDirectionWorld.x:F2},{debugDirectionWorld.z:F2})");
            y += line;

            GUI.Label(new Rect(x, y, width, line), "接触点部位选择 / Contact region picker");
            y += line;
            var topRow = GUI.Toolbar(
                new Rect(x, y, width, line),
                GetTopRowIndex(debugContactRegion),
                new[] { "Head", "Chest", "Hips", "Left Arm" });
            if (topRow >= 0)
                debugContactRegion = topRow switch
                {
                    0 => DebugHitContactRegion.Head,
                    1 => DebugHitContactRegion.Chest,
                    2 => DebugHitContactRegion.Hips,
                    3 => DebugHitContactRegion.LeftArm,
                    _ => debugContactRegion
                };
            y += line;

            var bottomRow = GUI.Toolbar(
                new Rect(x, y, width, line),
                GetBottomRowIndex(debugContactRegion),
                new[] { "Right Arm", "Left Leg", "Right Leg" });
            if (bottomRow >= 0)
                debugContactRegion = bottomRow switch
                {
                    0 => DebugHitContactRegion.RightArm,
                    1 => DebugHitContactRegion.LeftLeg,
                    2 => DebugHitContactRegion.RightLeg,
                    _ => debugContactRegion
                };
            y += line;

            GUI.Label(new Rect(x, y, width, line), "接触点快捷键 / Contact region hotkeys");
            y += line;
            GUI.Label(new Rect(x, y, width, line), $"Hotkeys: {headKey}/{chestKey}/{hipsKey}/{leftArmKey}/{rightArmKey}/{leftLegKey}/{rightLegKey}");
            y += line;

            GUI.Label(new Rect(x, y, width, line), "轻击调试按钮（不击倒）/ Light hit trigger");
            y += line;
            if (GUI.Button(new Rect(x, y, width, line), "Light Hit"))
            {
                if (useContinuousDirection)
                    root.DebugHitLight(-debugDirectionWorld, root.GetDebugContactPoint(debugContactRegion));
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
        }

        static int GetTopRowIndex(DebugHitContactRegion region) =>
            region switch
            {
                DebugHitContactRegion.Head => 0,
                DebugHitContactRegion.Chest => 1,
                DebugHitContactRegion.Hips => 2,
                DebugHitContactRegion.LeftArm => 3,
                _ => -1
            };

        static int GetBottomRowIndex(DebugHitContactRegion region) =>
            region switch
            {
                DebugHitContactRegion.RightArm => 0,
                DebugHitContactRegion.LeftLeg => 1,
                DebugHitContactRegion.RightLeg => 2,
                _ => -1
            };

        void DrawHitGizmos()
        {
            if (!showHitGizmos)
                return;

            if (root == null)
                root = GetComponent<CharacterControllerRoot>();
            if (root == null)
                return;

            var hasRagdollDebug = root.HasRagdollDebugHitInfo;
            var hasRootFallback = root.HasLastDebugHit;
            if (!hasRagdollDebug && !hasRootFallback)
                return;

            var debugTime = hasRagdollDebug ? root.RagdollDebugLastHitTime : root.LastDebugHitTime;
            if (Application.isPlaying && gizmoDuration > 0f && debugTime >= 0f)
            {
                var age = Time.time - debugTime;
                if (age > gizmoDuration)
                    return;
            }

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
                Gizmos.color = Color.cyan;
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
