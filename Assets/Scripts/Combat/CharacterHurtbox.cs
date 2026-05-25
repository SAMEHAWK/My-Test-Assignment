using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 角色部位受击盒：把 Collider 命中映射回角色与部位
    /// Character hurtbox: maps collider hits back to owner and body region
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterHurtbox : MonoBehaviour
    {
        [SerializeField] CharacterControllerRoot owner;
        [SerializeField] DebugHitContactRegion region = DebugHitContactRegion.Chest;
        [SerializeField] Transform referenceBone;
        [SerializeField] float impulseMultiplier = 1f;
        [SerializeField] float damageMultiplier = 1f;

        public CharacterControllerRoot Owner => owner;
        public DebugHitContactRegion Region => region;
        public Transform ReferenceBone => referenceBone != null ? referenceBone : transform;
        public float ImpulseMultiplier => Mathf.Max(0f, impulseMultiplier);
        public float DamageMultiplier => Mathf.Max(0f, damageMultiplier);

        void Awake()
        {
            ResolveOwnerIfMissing();
            if (referenceBone == null)
                referenceBone = transform;
        }

        void OnValidate()
        {
            ResolveOwnerIfMissing();
            impulseMultiplier = Mathf.Max(0f, impulseMultiplier);
            damageMultiplier = Mathf.Max(0f, damageMultiplier);
        }

        void ResolveOwnerIfMissing()
        {
            if (owner != null)
                return;

            owner = GetComponentInParent<CharacterControllerRoot>();
        }
    }
}
