using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// Ragdoll 恢复锚点：用于根节点对齐、朝向判断与相机跟随
    /// Ragdoll recovery anchor — root alignment, facing resolution, and camera follow source
    /// </summary>
    public readonly struct RagdollAnchor
    {
        public readonly Vector3 HipsWorldPosition;
        public readonly Vector3 FacingForward;
        public readonly bool IsValid;

        public RagdollAnchor(Vector3 hipsWorldPosition, Vector3 facingForward, bool isValid)
        {
            HipsWorldPosition = hipsWorldPosition;
            FacingForward = facingForward;
            IsValid = isValid;
        }
    }
}
