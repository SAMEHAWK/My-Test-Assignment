using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 受击方向调试浮标：仅负责可视化与角度同步（触发由 CharacterControllerDebug 面板负责）
    /// Hit-direction debug helper: visualization only; triggering is handled by CharacterControllerDebug panel
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitDirectionDebugHelper : MonoBehaviour
    {
        [Header("Target / 目标")]
        [SerializeField] CharacterControllerRoot targetRoot;

        [Header("Orbit / 环绕参数")]
        [SerializeField] float orbitRadius = 1.6f;
        [SerializeField] float heightOffset = 1.1f;
        [SerializeField] float yawDegrees;
        [SerializeField] bool lockToTarget = true;

        [Header("Visual / 可视化")]
        [SerializeField] Transform arrowVisual;
        [SerializeField] bool showGizmoArrow = true;
        [SerializeField] bool drawRuntimeRays = true;
        [SerializeField] bool invertIncoming;

        void Reset()
        {
            if (targetRoot == null)
                targetRoot = FindFirstObjectByType<CharacterControllerRoot>();
        }

        void Awake() => EnsureTargetRoot();

        void OnValidate() => EnsureTargetRoot();

        void Update()
        {
            EnsureTargetRoot();
            if (targetRoot == null)
                return;

            UpdatePose();
        }

        void EnsureTargetRoot()
        {
            if (targetRoot != null)
                return;

            targetRoot = FindFirstObjectByType<CharacterControllerRoot>();
        }

        public void SetTargetRoot(CharacterControllerRoot root) => targetRoot = root;

        public void SetYawDegrees(float yaw) => yawDegrees = Mathf.Repeat(yaw, 360f);

        public float GetYawDegrees() => yawDegrees;

        public void SetInvertIncoming(bool invert) => invertIncoming = invert;

        public bool GetInvertIncoming() => invertIncoming;

        /// <summary>
        /// 由箭头角度计算“来向”（来源 -> 角色）
        /// Build incoming direction (source -> character) from marker yaw
        /// </summary>
        public Vector3 GetWorldIncomingDirection()
        {
            var sourceBearing = Quaternion.Euler(0f, yawDegrees, 0f) * Vector3.forward;
            if (invertIncoming)
                sourceBearing = -sourceBearing;

            sourceBearing.y = 0f;
            if (sourceBearing.sqrMagnitude < 0.0001f)
                sourceBearing = Vector3.forward;

            // 统一语义：incoming = 来源 -> 角色
            // Unified semantics: incoming = source -> character
            return -sourceBearing.normalized;
        }

        void UpdatePose()
        {
            var target = targetRoot.transform;
            var incoming = GetWorldIncomingDirection();
            var sourceBearing = -incoming;
            var markerPosition = target.position + Vector3.up * heightOffset + sourceBearing * orbitRadius;

            if (lockToTarget)
                transform.position = markerPosition;

            transform.rotation = Quaternion.LookRotation(sourceBearing, Vector3.up);

            if (arrowVisual != null)
                arrowVisual.rotation = transform.rotation;

            if (drawRuntimeRays)
            {
                Debug.DrawLine(target.position + Vector3.up * heightOffset, markerPosition, Color.cyan);
                Debug.DrawRay(target.position + Vector3.up * heightOffset, incoming * orbitRadius, Color.red);
            }
        }

        void OnDrawGizmos()
        {
            if (!showGizmoArrow || targetRoot == null)
                return;

            var target = targetRoot.transform;
            var center = target.position + Vector3.up * heightOffset;
            var incoming = GetWorldIncomingDirection();
            var sourceBearing = -incoming;
            var markerPos = center + sourceBearing * orbitRadius;

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(center, markerPos);
            Gizmos.DrawSphere(markerPos, 0.06f);

            Gizmos.color = Color.red;
            Gizmos.DrawRay(center, incoming * orbitRadius);
        }
    }
}
