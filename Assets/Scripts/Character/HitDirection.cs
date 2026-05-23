using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 受击方向（相对角色朝向）
    /// Hit direction relative to character facing
    /// </summary>
    public enum HitDirection
    {
        Front,
        Back,
        Left,
        Right
    }

    /// <summary>
    /// 受击方向工具方法
    /// Hit direction utilities
    /// </summary>
    public static class HitDirectionUtility
    {
        /// <summary>
        /// 将枚举方向转为世界空间单位向量（基于角色 Transform）
        /// Converts enum direction to world-space unit vector using character transform
        /// </summary>
        public static Vector3 ToWorldVector(HitDirection direction, Transform character)
        {
            return direction switch
            {
                HitDirection.Front => character.forward,
                HitDirection.Back => -character.forward,
                HitDirection.Left => -character.right,
                HitDirection.Right => character.right,
                _ => character.forward
            };
        }

        /// <summary>
        /// 从 HitContext 解析打击来源世界方向（优先 WorldIncoming，否则枚举）
        /// Resolve incoming hit world direction from context
        /// </summary>
        public static Vector3 ResolveIncomingWorld(in HitContext hitContext, Transform character)
        {
            if (hitContext.HasWorldIncoming)
            {
                var dir = hitContext.WorldIncomingDirection;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f)
                    return dir.normalized;
            }

            return ToWorldVector(hitContext.Direction, character);
        }

        /// <summary>
        /// Animator 2D Blend：枚举四角 (HitBlendX, HitBlendZ)
        /// Animator 2D blend corners from enum
        /// </summary>
        public static Vector2 ToAnimatorBlendLocal(HitDirection direction, Transform character)
        {
            _ = character;
            return direction switch
            {
                HitDirection.Front => new Vector2(0f, 1f),
                HitDirection.Back => new Vector2(0f, -1f),
                HitDirection.Left => new Vector2(-1f, 0f),
                HitDirection.Right => new Vector2(1f, 0f),
                _ => new Vector2(0f, 1f)
            };
        }

        /// <summary>
        /// Animator 2D Blend：世界打击来源 → 角色本地 XZ 归一化
        /// World incoming direction to normalized local XZ blend parameters
        /// </summary>
        public static Vector2 ToAnimatorBlendLocal(Vector3 worldIncoming, Transform character)
        {
            var local = character.InverseTransformDirection(worldIncoming);
            local.y = 0f;
            if (local.sqrMagnitude < 0.0001f)
                return new Vector2(0f, 1f);
            local.Normalize();
            return new Vector2(local.x, local.z);
        }

        /// <summary>
        /// Animator 2D Blend：优先连续世界方向，否则枚举四角
        /// Blend params from context — world vector preferred, else enum corner
        /// </summary>
        public static Vector2 ToAnimatorBlendLocal(in HitContext hitContext, Transform character)
        {
            if (hitContext.HasWorldIncoming)
                return ToAnimatorBlendLocal(hitContext.WorldIncomingDirection, character);
            return ToAnimatorBlendLocal(hitContext.Direction, character);
        }

        /// <summary>
        /// 轻击反冲旋转：打击方向按 facingBone 解析，再投影到 applyBone 本地轴后转欧拉
        /// Recoil quaternion — incoming resolved on facing bone, components in apply bone local space
        /// </summary>
        public static Quaternion ComputeRecoilLocalOffset(
            Transform facingBone,
            Transform applyBone,
            in HitContext hitContext,
            float pitchDegrees,
            float rollDegrees)
        {
            if (applyBone == null)
                return Quaternion.identity;

            var facing = facingBone != null ? facingBone : applyBone;

            var incoming = ResolveIncomingWorld(in hitContext, facing);
            incoming.y = 0f;
            if (incoming.sqrMagnitude < 0.0001f)
                incoming = facing.forward;

            var recoil = -incoming.normalized;
            var local = applyBone.InverseTransformDirection(recoil);
            var pitch = local.z * pitchDegrees;
            var roll = -local.x * rollDegrees;
            return Quaternion.Euler(pitch, 0f, roll);
        }

        /// <summary>
        /// 世界方向量化为四向枚举（用于兼容旧逻辑/调试显示）
        /// Quantize world direction into 4-way enum
        /// </summary>
        public static HitDirection ResolveDirectionFromWorld(Vector3 worldDirection, Transform character)
        {
            if (character == null)
                return HitDirection.Front;

            var dir = worldDirection;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
                dir = character.forward;

            var local = character.InverseTransformDirection(dir.normalized);
            if (Mathf.Abs(local.z) >= Mathf.Abs(local.x))
                return local.z >= 0f ? HitDirection.Front : HitDirection.Back;
            return local.x >= 0f ? HitDirection.Right : HitDirection.Left;
        }
    }
}
