using UnityEngine;

namespace ActiveRagdoll.UI
{
    /// <summary>
    /// 世界空间 Canvas 始终朝向摄像机
    /// Keeps a world-space Canvas facing the camera
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldSpaceCanvasBillboard : MonoBehaviour
    {
        [SerializeField] UnityEngine.Camera targetCamera;
        [SerializeField] bool useMainCameraIfEmpty = true;
        [SerializeField] bool lockWorldUp = true;

        void LateUpdate()
        {
            var cameraToUse = ResolveCamera();
            if (cameraToUse == null)
                return;

            var forward = transform.position - cameraToUse.transform.position;
            if (forward.sqrMagnitude < 0.0001f)
                return;

            if (lockWorldUp)
            {
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f)
                    return;
                transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
                return;
            }

            transform.rotation = Quaternion.LookRotation(forward.normalized, cameraToUse.transform.up);
        }

        UnityEngine.Camera ResolveCamera()
        {
            if (targetCamera != null)
                return targetCamera;

            return useMainCameraIfEmpty ? UnityEngine.Camera.main : null;
        }
    }
}
