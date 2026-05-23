using ActiveRagdoll.Character;
using UnityEngine;

namespace ActiveRagdoll.Camera
{
    /// <summary>
    /// 镜头代理目标驱动：击飞期跟随 ragdoll，恢复期平滑回到 Player
    /// Camera proxy driver: follow ragdoll during knockdown, blend back to player during recovery
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraFollowTargetDriver : MonoBehaviour
    {
        [Header("References / 引用")]
        [Tooltip("玩家根组件 — Player root component")]
        [SerializeField] CharacterControllerRoot targetRoot;

        [Header("Offsets / 偏移")]
        [Tooltip("代理目标世界 Y 偏移（通常 0~1）— World-space Y offset for proxy target")]
        [SerializeField] float worldYOffset = 0.6f;
        [Tooltip("击飞期是否锁定 Y 到 Player（推荐开，避免 ragdoll 翻滚带来的镜头抖动）— Lock Y to player during knockdown")]
        [SerializeField] bool lockYToPlayerDuringKnockdown = true;

        [Header("Smoothing / 平滑")]
        [Tooltip("击飞阶段跟随锐度（越大越跟手）— Knockdown follow sharpness")]
        [SerializeField] float knockdownFollowSharpness = 24f;
        [Tooltip("Y 轴独立平滑锐度（越小越稳）— Independent Y-axis follow sharpness")]
        [SerializeField] float yAxisFollowSharpness = 6f;
        [Tooltip("恢复阶段回切锐度（越大越快回到 Player）— Recovery return sharpness")]
        [SerializeField] float recoveryReturnSharpness = 8f;
        [Tooltip("常态跟随锐度 — Default follow sharpness")]
        [SerializeField] float locomotionFollowSharpness = 14f;
        [Tooltip("回切收敛距离（米）— Snap distance when returning to player")]
        [SerializeField] float recoverySnapDistance = 0.06f;

        bool _hasEnteredKnockdownSinceLastSync;

        void Awake()
        {
            TryAutoBindRoot();
            ForceSnapToPlayer();
        }

        void LateUpdate()
        {
            if (targetRoot == null)
                return;

            var desiredPosition = ResolveDesiredPosition();
            var xzSharpness = ResolveCurrentSharpness();
            transform.position = DampToByAxis(transform.position, desiredPosition, xzSharpness, yAxisFollowSharpness, Time.deltaTime);

            var playerTransform = targetRoot.transform;
            transform.rotation = playerTransform != null ? playerTransform.rotation : Quaternion.identity;

            // 起身阶段收敛后清标记，避免再次使用回切逻辑
            // Clear flag after convergence in recovery stage
            if (targetRoot.IsInRecoveryPhase && Vector3.Distance(transform.position, desiredPosition) <= recoverySnapDistance)
                _hasEnteredKnockdownSinceLastSync = false;
        }

        void OnValidate()
        {
            knockdownFollowSharpness = Mathf.Max(0f, knockdownFollowSharpness);
            yAxisFollowSharpness = Mathf.Max(0f, yAxisFollowSharpness);
            recoveryReturnSharpness = Mathf.Max(0f, recoveryReturnSharpness);
            locomotionFollowSharpness = Mathf.Max(0f, locomotionFollowSharpness);
            recoverySnapDistance = Mathf.Max(0.001f, recoverySnapDistance);
        }

        void TryAutoBindRoot()
        {
            if (targetRoot != null)
                return;

            targetRoot = FindFirstObjectByType<CharacterControllerRoot>();
        }

        Vector3 ResolveDesiredPosition()
        {
            var playerTransform = targetRoot.transform;
            var basePosition = playerTransform != null ? playerTransform.position : Vector3.zero;
            var playerY = basePosition.y;

            if (targetRoot.IsInKnockdownPhase)
            {
                var ragdollAnchor = targetRoot.GetCameraFollowAnchor();
                if (ragdollAnchor != null)
                {
                    basePosition = ragdollAnchor.position;
                    if (lockYToPlayerDuringKnockdown)
                        basePosition.y = playerY;
                }
                _hasEnteredKnockdownSinceLastSync = true;
            }

            return basePosition + Vector3.up * worldYOffset;
        }

        float ResolveCurrentSharpness()
        {
            if (targetRoot.IsInKnockdownPhase)
                return knockdownFollowSharpness;

            if (targetRoot.IsInRecoveryPhase || _hasEnteredKnockdownSinceLastSync)
                return recoveryReturnSharpness;

            return locomotionFollowSharpness;
        }

        void ForceSnapToPlayer()
        {
            if (targetRoot == null)
                return;

            var playerTransform = targetRoot.transform;
            if (playerTransform == null)
                return;

            transform.position = playerTransform.position + Vector3.up * worldYOffset;
            transform.rotation = playerTransform.rotation;
            _hasEnteredKnockdownSinceLastSync = false;
        }

        static Vector3 DampTo(Vector3 current, Vector3 target, float sharpness, float deltaTime)
        {
            if (sharpness <= 0f || deltaTime <= 0f)
                return target;

            var t = 1f - Mathf.Exp(-sharpness * deltaTime);
            return Vector3.Lerp(current, target, t);
        }

        static Vector3 DampToByAxis(
            Vector3 current,
            Vector3 target,
            float xzSharpness,
            float ySharpness,
            float deltaTime)
        {
            if (deltaTime <= 0f)
                return target;

            var next = current;

            var xzCurrent = new Vector2(current.x, current.z);
            var xzTarget = new Vector2(target.x, target.z);
            var xzT = xzSharpness <= 0f ? 1f : 1f - Mathf.Exp(-xzSharpness * deltaTime);
            var xzNext = Vector2.Lerp(xzCurrent, xzTarget, xzT);
            next.x = xzNext.x;
            next.z = xzNext.y;

            var yT = ySharpness <= 0f ? 1f : 1f - Mathf.Exp(-ySharpness * deltaTime);
            next.y = Mathf.Lerp(current.y, target.y, yT);

            return next;
        }
    }
}
