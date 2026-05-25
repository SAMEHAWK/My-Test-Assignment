using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 一次受击的只读上下文
    /// Read-only context for a single hit
    /// </summary>
    public readonly struct HitContext
    {
        public HitType Type { get; }
        public HitDirection Direction { get; }

        /// <summary>
        /// 可选：打击来源世界方向；非零时动画 blend 与物理优先使用
        /// Optional world-space incoming direction for blend and physics
        /// </summary>
        public Vector3 WorldIncomingDirection { get; }

        /// <summary>
        /// 可选：攻击来源到角色的世界方向；用于未破平衡时的受击表现
        /// Optional source-to-character world direction; used for non-depleted hit reactions
        /// </summary>
        public Vector3 WorldSourceDirection { get; }

        /// <summary>
        /// 是否使用 WorldIncomingDirection
        /// Whether world incoming direction is set
        /// </summary>
        public bool HasWorldIncoming => WorldIncomingDirection.sqrMagnitude > 0.0001f;

        public bool HasWorldSource => WorldSourceDirection.sqrMagnitude > 0.0001f;

        public Vector3 ContactPoint { get; }
        public float Impulse { get; }
        public bool BypassBalance { get; }
        public Object Source { get; }

        public HitContext(
            HitType type,
            HitDirection direction,
            Vector3 contactPoint,
            float impulse = 1f,
            bool bypassBalance = false,
            Object source = null,
            Vector3 worldIncomingDirection = default,
            Vector3 worldSourceDirection = default)
        {
            Type = type;
            Direction = direction;
            ContactPoint = contactPoint;
            Impulse = impulse;
            BypassBalance = bypassBalance;
            Source = source;
            WorldIncomingDirection = worldIncomingDirection;
            WorldSourceDirection = worldSourceDirection;
        }

        /// <summary>
        /// 调试：轻击
        /// Debug: light hit
        /// </summary>
        public static HitContext DebugLight(HitDirection direction, Transform character)
        {
            return new HitContext(
                HitType.Light,
                direction,
                character.position + Vector3.up,
                impulse: 1f,
                source: null);
        }

        /// <summary>
        /// 调试：重击
        /// Debug: heavy hit
        /// </summary>
        public static HitContext DebugHeavy(HitDirection direction, Transform character)
        {
            return new HitContext(
                HitType.Heavy,
                direction,
                character.position + Vector3.up * 1.2f,
                impulse: 2f,
                source: null);
        }

        /// <summary>
        /// 调试：强制击倒（轻击级冲量）
        /// Debug: forced knockdown with light impulse semantics
        /// </summary>
        public static HitContext DebugForceKnockdownLight(Vector3 worldDirection, Transform character, float impulse = 2f)
        {
            var dir = worldDirection.sqrMagnitude > 0.01f ? worldDirection.normalized : character.forward;
            var hitDir = ResolveDirectionFromWorldVector(dir, character);
            return new HitContext(
                HitType.ForceKnockdownLight,
                hitDir,
                character.position + Vector3.up,
                impulse: Mathf.Max(impulse, 0f),
                bypassBalance: true,
                source: null,
                worldIncomingDirection: dir);
        }

        public static HitContext DebugForceKnockdownLight(Vector3 worldDirection, Transform character, float impulse, Vector3 contactPoint)
        {
            var dir = worldDirection.sqrMagnitude > 0.01f ? worldDirection.normalized : character.forward;
            var hitDir = ResolveDirectionFromWorldVector(dir, character);
            return new HitContext(
                HitType.ForceKnockdownLight,
                hitDir,
                contactPoint,
                impulse: Mathf.Max(impulse, 0f),
                bypassBalance: true,
                source: null,
                worldIncomingDirection: dir);
        }

        /// <summary>
        /// 调试：强制击倒（重击级冲量）
        /// Debug: forced knockdown with heavy impulse semantics
        /// </summary>
        public static HitContext DebugForceKnockdownHeavy(Vector3 worldDirection, Transform character, float impulse = 6f)
        {
            var dir = worldDirection.sqrMagnitude > 0.01f ? worldDirection.normalized : character.forward;
            var hitDir = ResolveDirectionFromWorldVector(dir, character);
            return new HitContext(
                HitType.ForceKnockdownHeavy,
                hitDir,
                character.position + Vector3.up,
                impulse: Mathf.Max(impulse, 0f),
                bypassBalance: true,
                source: null,
                worldIncomingDirection: dir);
        }

        public static HitContext DebugForceKnockdownHeavy(Vector3 worldDirection, Transform character, float impulse, Vector3 contactPoint)
        {
            var dir = worldDirection.sqrMagnitude > 0.01f ? worldDirection.normalized : character.forward;
            var hitDir = ResolveDirectionFromWorldVector(dir, character);
            return new HitContext(
                HitType.ForceKnockdownHeavy,
                hitDir,
                contactPoint,
                impulse: Mathf.Max(impulse, 0f),
                bypassBalance: true,
                source: null,
                worldIncomingDirection: dir);
        }

        static HitDirection ResolveDirectionFromWorldVector(Vector3 worldDir, Transform character)
        {
            var local = character.InverseTransformDirection(worldDir);
            if (Mathf.Abs(local.z) >= Mathf.Abs(local.x))
                return local.z >= 0f ? HitDirection.Front : HitDirection.Back;
            return local.x >= 0f ? HitDirection.Right : HitDirection.Left;
        }
    }
}
