using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 调试受击注入驱动 — 从 CharacterControllerRoot 提取，仅被 CharacterControllerDebug 使用
    /// Debug hit injection driver — extracted from CharacterControllerRoot, used only by CharacterControllerDebug
    /// </summary>
    public sealed class CharacterDebugHitDriver
    {
        readonly CharacterControllerRoot _root;
        readonly Animator _animator;
        readonly CharacterRagdollSystem _ragdollSystem;

        bool _hasLastDebugHit;
        float _lastDebugHitTime = -1f;
        HitContext _lastDebugHit;
        Vector3 _lastResolvedDebugContactPoint;
        string _lastResolvedDebugContactSource = "Unknown";
        string _lastResolvedDebugContactBoneName = string.Empty;

        public bool HasLastDebugHit => _hasLastDebugHit;
        public float LastDebugHitTime => _lastDebugHitTime;
        public HitContext LastDebugHit => _lastDebugHit;
        public Vector3 LastResolvedDebugContactPoint => _lastResolvedDebugContactPoint;
        public string LastResolvedDebugContactSource => _lastResolvedDebugContactSource;
        public string LastResolvedDebugContactBoneName => _lastResolvedDebugContactBoneName;

        public CharacterDebugHitDriver(
            CharacterControllerRoot root,
            Animator animator,
            CharacterRagdollSystem ragdollSystem)
        {
            _root = root;
            _animator = animator;
            _ragdollSystem = ragdollSystem;
        }

        /// <summary>
        /// 由 Root.ReceiveHit 调用以记录最近一次调试受击数据
        /// Called by Root.ReceiveHit to record the latest debug hit data
        /// </summary>
        public void RecordDebugHit(in HitContext resolvedHitContext)
        {
            _hasLastDebugHit = true;
            _lastDebugHitTime = Time.time;
            _lastDebugHit = resolvedHitContext;
        }

        #region Light Hit / 轻击

        public void DebugHitLight(HitDirection direction) =>
            DebugHitLight(HitDirectionUtility.ToWorldVector(direction, _root.transform));

        public void DebugHitLight(HitDirection direction, DebugHitContactRegion contactRegion) =>
            DebugHitLight(HitDirectionUtility.ToWorldVector(direction, _root.transform), GetDebugContactPoint(contactRegion));

        public void DebugHitLight(Vector3 worldIncomingDirection)
        {
            var incoming = worldIncomingDirection;
            incoming.y = 0f;
            if (incoming.sqrMagnitude < 0.0001f)
                incoming = _root.transform.forward;
            incoming.Normalize();

            var dir = HitDirectionUtility.ResolveDirectionFromWorld(incoming, _root.transform);
            _root.ReceiveHit(new HitContext(
                HitType.Light,
                dir,
                _root.transform.position + Vector3.up,
                impulse: 1f,
                source: null,
                worldIncomingDirection: incoming));
        }

        public void DebugHitLight(Vector3 worldIncomingDirection, Vector3 contactPoint)
        {
            var incoming = worldIncomingDirection;
            incoming.y = 0f;
            if (incoming.sqrMagnitude < 0.0001f)
                incoming = _root.transform.forward;
            incoming.Normalize();

            var dir = HitDirectionUtility.ResolveDirectionFromWorld(incoming, _root.transform);
            _root.ReceiveHit(new HitContext(
                HitType.Light,
                dir,
                contactPoint,
                impulse: 1f,
                source: null,
                worldIncomingDirection: incoming));
        }

        #endregion

        #region Heavy Hit / 重击

        public void DebugHitHeavy(HitDirection direction) =>
            DebugHitHeavy(-HitDirectionUtility.ToWorldVector(direction, _root.transform));

        public void DebugHitHeavy(HitDirection direction, DebugHitContactRegion contactRegion) =>
            DebugHitHeavy(-HitDirectionUtility.ToWorldVector(direction, _root.transform), GetDebugContactPoint(contactRegion));

        public void DebugHitHeavy(HitDirection direction, float impulseOverride, DebugHitContactRegion contactRegion) =>
            DebugHitHeavy(-HitDirectionUtility.ToWorldVector(direction, _root.transform), impulseOverride, GetDebugContactPoint(contactRegion));

        public void DebugHitHeavy(Vector3 worldIncomingDirection)
        {
            var incoming = worldIncomingDirection;
            incoming.y = 0f;
            if (incoming.sqrMagnitude < 0.0001f)
                incoming = _root.transform.forward;
            incoming.Normalize();

            var dir = HitDirectionUtility.ResolveDirectionFromWorld(incoming, _root.transform);
            _root.ReceiveHit(new HitContext(
                HitType.Heavy,
                dir,
                _root.transform.position + Vector3.up * 1.2f,
                impulse: 2f,
                source: null,
                worldIncomingDirection: incoming));
        }

        public void DebugHitHeavy(Vector3 worldIncomingDirection, float impulseOverride)
        {
            var incoming = worldIncomingDirection;
            incoming.y = 0f;
            if (incoming.sqrMagnitude < 0.0001f)
                incoming = _root.transform.forward;
            incoming.Normalize();

            var dir = HitDirectionUtility.ResolveDirectionFromWorld(incoming, _root.transform);
            _root.ReceiveHit(new HitContext(
                HitType.Heavy,
                dir,
                _root.transform.position + Vector3.up * 1.2f,
                impulse: Mathf.Max(0f, impulseOverride),
                source: null,
                worldIncomingDirection: incoming));
        }

        public void DebugHitHeavy(Vector3 worldIncomingDirection, Vector3 contactPoint)
        {
            var incoming = worldIncomingDirection;
            incoming.y = 0f;
            if (incoming.sqrMagnitude < 0.0001f)
                incoming = _root.transform.forward;
            incoming.Normalize();

            var dir = HitDirectionUtility.ResolveDirectionFromWorld(incoming, _root.transform);
            _root.ReceiveHit(new HitContext(
                HitType.Heavy,
                dir,
                contactPoint,
                impulse: 2f,
                source: null,
                worldIncomingDirection: incoming));
        }

        public void DebugHitHeavy(Vector3 worldIncomingDirection, float impulseOverride, Vector3 contactPoint)
        {
            var incoming = worldIncomingDirection;
            incoming.y = 0f;
            if (incoming.sqrMagnitude < 0.0001f)
                incoming = _root.transform.forward;
            incoming.Normalize();

            var dir = HitDirectionUtility.ResolveDirectionFromWorld(incoming, _root.transform);
            _root.ReceiveHit(new HitContext(
                HitType.Heavy,
                dir,
                contactPoint,
                impulse: Mathf.Max(0f, impulseOverride),
                source: null,
                worldIncomingDirection: incoming));
        }

        #endregion

        #region Force Knockdown / 强制击倒

        public void DebugForceKnockdownLight(Vector3 worldDirection, float impulseOverride) =>
            _root.ReceiveHit(HitContext.DebugForceKnockdownLight(worldDirection, _root.transform, impulseOverride));

        public void DebugForceKnockdownLight(Vector3 worldDirection, float impulseOverride, DebugHitContactRegion contactRegion) =>
            _root.ReceiveHit(HitContext.DebugForceKnockdownLight(worldDirection, _root.transform, impulseOverride, GetDebugContactPoint(contactRegion)));

        public void DebugForceKnockdownHeavy(Vector3 worldDirection, float impulseOverride) =>
            _root.ReceiveHit(HitContext.DebugForceKnockdownHeavy(worldDirection, _root.transform, impulseOverride));

        public void DebugForceKnockdownHeavy(Vector3 worldDirection, float impulseOverride, DebugHitContactRegion contactRegion) =>
            _root.ReceiveHit(HitContext.DebugForceKnockdownHeavy(worldDirection, _root.transform, impulseOverride, GetDebugContactPoint(contactRegion)));

        #endregion

        #region Contact Point Resolution / 接触点解析

        /// <summary>
        /// 调试用接触点：优先 Ragdoll 物理骨骼，其次 Humanoid 骨骼，最后回退到根节点偏移
        /// Debug contact point: physics bones first, then humanoid, then root offsets
        /// </summary>
        public Vector3 GetDebugContactPoint(DebugHitContactRegion region)
        {
            if (_ragdollSystem != null
                && _ragdollSystem.TryGetDebugContactPoint(region, out var ragdollPoint, out var ragdollBoneName))
            {
                RecordResolvedDebugContact(
                    ragdollPoint,
                    $"RagdollPhysics:{ragdollBoneName}",
                    ragdollBoneName);
                return ragdollPoint;
            }

            var modelAnimator = _animator;
            if (modelAnimator != null && modelAnimator.isHuman && modelAnimator.avatar != null)
            {
                Transform bone = region switch
                {
                    DebugHitContactRegion.Hips => modelAnimator.GetBoneTransform(HumanBodyBones.Hips),
                    DebugHitContactRegion.Head => modelAnimator.GetBoneTransform(HumanBodyBones.Head),
                    DebugHitContactRegion.Chest => modelAnimator.GetBoneTransform(HumanBodyBones.UpperChest) ?? modelAnimator.GetBoneTransform(HumanBodyBones.Chest) ?? modelAnimator.GetBoneTransform(HumanBodyBones.Spine),
                    DebugHitContactRegion.LeftArm => modelAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm) ?? modelAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm),
                    DebugHitContactRegion.RightArm => modelAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm) ?? modelAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm),
                    DebugHitContactRegion.LeftElbow => modelAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm) ?? modelAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm),
                    DebugHitContactRegion.RightElbow => modelAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm) ?? modelAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm),
                    DebugHitContactRegion.LeftWrist => modelAnimator.GetBoneTransform(HumanBodyBones.LeftHand) ?? modelAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm),
                    DebugHitContactRegion.RightWrist => modelAnimator.GetBoneTransform(HumanBodyBones.RightHand) ?? modelAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm),
                    DebugHitContactRegion.LeftLeg => modelAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg) ?? modelAnimator.GetBoneTransform(HumanBodyBones.LeftLowerLeg),
                    DebugHitContactRegion.RightLeg => modelAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg) ?? modelAnimator.GetBoneTransform(HumanBodyBones.RightLowerLeg),
                    DebugHitContactRegion.LeftKnee => modelAnimator.GetBoneTransform(HumanBodyBones.LeftLowerLeg) ?? modelAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg),
                    DebugHitContactRegion.RightKnee => modelAnimator.GetBoneTransform(HumanBodyBones.RightLowerLeg) ?? modelAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg),
                    DebugHitContactRegion.LeftAnkle => modelAnimator.GetBoneTransform(HumanBodyBones.LeftFoot) ?? modelAnimator.GetBoneTransform(HumanBodyBones.LeftLowerLeg),
                    DebugHitContactRegion.RightAnkle => modelAnimator.GetBoneTransform(HumanBodyBones.RightFoot) ?? modelAnimator.GetBoneTransform(HumanBodyBones.RightLowerLeg),
                    _ => null
                };

                if (bone != null)
                {
                    RecordResolvedDebugContact(
                        bone.position,
                        $"Humanoid:{bone.name}",
                        bone.name);
                    return bone.position;
                }
            }

            var rootPos = _root.transform.position;
            var fallbackPoint = region switch
            {
                DebugHitContactRegion.Hips => rootPos + Vector3.up * 0.95f,
                DebugHitContactRegion.Head => rootPos + Vector3.up * 1.7f,
                DebugHitContactRegion.Chest => rootPos + Vector3.up * 1.2f,
                DebugHitContactRegion.LeftArm => rootPos + Vector3.up * 1.2f - _root.transform.right * 0.35f,
                DebugHitContactRegion.RightArm => rootPos + Vector3.up * 1.2f + _root.transform.right * 0.35f,
                DebugHitContactRegion.LeftElbow => rootPos + Vector3.up * 1.12f - _root.transform.right * 0.42f,
                DebugHitContactRegion.RightElbow => rootPos + Vector3.up * 1.12f + _root.transform.right * 0.42f,
                DebugHitContactRegion.LeftWrist => rootPos + Vector3.up * 0.98f - _root.transform.right * 0.48f,
                DebugHitContactRegion.RightWrist => rootPos + Vector3.up * 0.98f + _root.transform.right * 0.48f,
                DebugHitContactRegion.LeftLeg => rootPos + Vector3.up * 0.65f - _root.transform.right * 0.18f,
                DebugHitContactRegion.RightLeg => rootPos + Vector3.up * 0.65f + _root.transform.right * 0.18f,
                DebugHitContactRegion.LeftKnee => rootPos + Vector3.up * 0.48f - _root.transform.right * 0.16f,
                DebugHitContactRegion.RightKnee => rootPos + Vector3.up * 0.48f + _root.transform.right * 0.16f,
                DebugHitContactRegion.LeftAnkle => rootPos + Vector3.up * 0.2f - _root.transform.right * 0.14f,
                DebugHitContactRegion.RightAnkle => rootPos + Vector3.up * 0.2f + _root.transform.right * 0.14f,
                _ => rootPos + Vector3.up
            };
            RecordResolvedDebugContact(fallbackPoint, "FallbackOffset", string.Empty);
            return fallbackPoint;
        }

        void RecordResolvedDebugContact(Vector3 point, string source, string boneName)
        {
            _lastResolvedDebugContactPoint = point;
            _lastResolvedDebugContactSource = string.IsNullOrWhiteSpace(source) ? "Unknown" : source;
            _lastResolvedDebugContactBoneName = boneName ?? string.Empty;
        }

        #endregion
    }
}
