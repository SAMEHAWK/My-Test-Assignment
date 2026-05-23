using UnityEngine;
using System;
using System.Collections.Generic;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 旧单骨架布娃娃实现（仅用于 C8 过渡期 legacy 回退）
    /// Legacy single-skeleton ragdoll implementation (C8 transition fallback only)
    /// </summary>
    public sealed class RagdollModule : ICharacterModule
    {
        readonly Transform _transform;
        readonly Rigidbody[] _ragdollBodies;
        readonly float _minKnockdownDuration;
        readonly float _placeholderSettleDuration;
        readonly float _settleSpeedThreshold;
        readonly float _heavyPartialRadius;
        readonly int _heavyPartialMaxBodies;
        readonly float _heavyPartialPhysicsHoldDuration;
        readonly float _heavyPartialPrimaryImpulseMultiplier;
        readonly float _heavyPartialSecondaryImpulseMultiplier;
        readonly float _heavyPartialAngularImpulseMultiplier;
        readonly int _heavyPartialSolverIterations;
        readonly int _heavyPartialSolverVelocityIterations;
        readonly float _heavyPartialBlendBackDuration;
        readonly AnimationCurve _heavyPartialBlendBackCurve;

        bool _isActive;
        float _knockdownTimer;
        readonly List<Rigidbody> _heavyPartialBodies = new();
        readonly List<HeavyPartialBodyState> _heavyPartialBodyStates = new();
        bool _heavyPartialActive;
        bool _heavyPartialBlendActive;
        bool _heavyPartialPhysicsActive;
        float _heavyPartialPhysicsElapsed;
        float _heavyPartialBlendElapsed;
        float _heavyPartialBlendWeight;

        struct HeavyPartialBodyState
        {
            public Rigidbody Body;
            public RigidbodyInterpolation Interpolation;
            public CollisionDetectionMode CollisionDetectionMode;
            public int SolverIterations;
            public int SolverVelocityIterations;
            public Quaternion PhysicsLocalRotation;
        }

        public bool HasRagdollSetup => _ragdollBodies != null && _ragdollBodies.Length > 0;
        public bool IsHeavyPartialActive => _heavyPartialActive;
        public bool IsHeavyPartialBlendComplete => !_heavyPartialActive;

        public bool IsSettled
        {
            get
            {
                if (!_isActive)
                    return true;
                if (_knockdownTimer < _minKnockdownDuration)
                    return false;
                if (!HasRagdollSetup)
                    return _knockdownTimer >= _placeholderSettleDuration;
                return AllBodiesBelowSpeedThreshold();
            }
        }

        public RagdollModule(
            Transform transform,
            Rigidbody[] ragdollBodies,
            float minKnockdownDuration,
            float placeholderSettleDuration,
            float settleSpeedThreshold,
            float heavyPartialBlendBackDuration,
            AnimationCurve heavyPartialBlendBackCurve,
            float heavyPartialRadius,
            int heavyPartialMaxBodies,
            float heavyPartialPhysicsHoldDuration,
            float heavyPartialPrimaryImpulseMultiplier,
            float heavyPartialSecondaryImpulseMultiplier,
            float heavyPartialAngularImpulseMultiplier,
            int heavyPartialSolverIterations,
            int heavyPartialSolverVelocityIterations)
        {
            _transform = transform;
            _ragdollBodies = ragdollBodies;
            // 兜底最小时长，避免旧场景未序列化该字段时出现瞬时恢复
            // Safety floor prevents instant recovery on legacy scenes
            _minKnockdownDuration = Mathf.Max(0.6f, minKnockdownDuration);
            _placeholderSettleDuration = placeholderSettleDuration;
            _settleSpeedThreshold = settleSpeedThreshold;
            _heavyPartialBlendBackDuration = Mathf.Max(0.01f, heavyPartialBlendBackDuration);
            _heavyPartialBlendBackCurve = heavyPartialBlendBackCurve;
            _heavyPartialRadius = Mathf.Max(0.2f, heavyPartialRadius);
            _heavyPartialMaxBodies = Mathf.Max(1, heavyPartialMaxBodies);
            _heavyPartialPhysicsHoldDuration = Mathf.Max(0.02f, heavyPartialPhysicsHoldDuration);
            _heavyPartialPrimaryImpulseMultiplier = Mathf.Max(0f, heavyPartialPrimaryImpulseMultiplier);
            _heavyPartialSecondaryImpulseMultiplier = Mathf.Max(0f, heavyPartialSecondaryImpulseMultiplier);
            _heavyPartialAngularImpulseMultiplier = Mathf.Max(0f, heavyPartialAngularImpulseMultiplier);
            _heavyPartialSolverIterations = Mathf.Max(1, heavyPartialSolverIterations);
            _heavyPartialSolverVelocityIterations = Mathf.Max(1, heavyPartialSolverVelocityIterations);
        }

        public void OnEnterState(CharacterState state, in HitContext hitContext)
        {
            switch (state)
            {
                case CharacterState.Knockdown:
                case CharacterState.ForcedKnockdown:
                    EnableAll(in hitContext);
                    break;
                case CharacterState.HeavyStagger:
                    EnableHeavyPartial(in hitContext);
                    break;

                default:
                    SetRagdollActive(false);
                    break;
            }
        }

        public void OnExitState(CharacterState state)
        {
            if (state is CharacterState.Knockdown or CharacterState.ForcedKnockdown
                or CharacterState.HeavyStagger)
            {
                SetRagdollActive(false);
            }
        }

        public void OnTickState(CharacterState state, float deltaTime)
        {
            if (_isActive && (state == CharacterState.Knockdown || state == CharacterState.ForcedKnockdown))
                _knockdownTimer += deltaTime;

            if (state == CharacterState.HeavyStagger && _heavyPartialActive)
            {
                if (_heavyPartialPhysicsActive)
                    TickHeavyPartialPhysicsHold(deltaTime);
                else if (_heavyPartialBlendActive)
                    TickHeavyPartialBlendBack(deltaTime);
            }
        }

        public void OnFixedTickState(CharacterState state, float fixedDeltaTime) { }

        /// <summary>
        /// 击倒：全身刚体 + 冲量（含强制轻/重击倒）
        /// Knockdown — enable all ragdoll bodies and apply impulse
        /// </summary>
        void EnableAll(in HitContext hitContext)
        {
            _knockdownTimer = 0f;
            SetRagdollActive(true);

            if (_ragdollBodies == null)
                return;

            var worldDir = HitDirectionUtility.ResolveIncomingWorld(in hitContext, _transform);
            var impulse = ResolveImpulse(in hitContext);

            var force = worldDir.normalized * impulse;

            foreach (var body in _ragdollBodies)
            {
                if (body == null)
                    continue;
                SetDynamic(body);
                body.WakeUp();
                body.AddForceAtPosition(force, hitContext.ContactPoint, ForceMode.Impulse);
            }
        }

        float ResolveImpulse(in HitContext hitContext)
        {
            // 0 也是合法冲量（用于“只看分权与融合，不施加推力”的调试场景）
            // Zero is a valid impulse for ownership/blend-back testing with no push force
            if (hitContext.Impulse >= 0f)
                return hitContext.Impulse;

            return hitContext.Type switch
            {
                HitType.Heavy => 6f,
                HitType.ForceKnockdownHeavy => 6f,
                HitType.ForceKnockdownLight => 2f,
                _ => 2f
            };
        }

        bool AllBodiesBelowSpeedThreshold()
        {
            foreach (var body in _ragdollBodies)
            {
                if (body == null || body.isKinematic)
                    continue;
                if (body.linearVelocity.magnitude > _settleSpeedThreshold)
                    return false;
            }

            return true;
        }

        static void ZeroVelocities(Rigidbody body)
        {
            if (body == null || body.isKinematic)
                return;

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        static void SetDynamic(Rigidbody body)
        {
            if (body == null)
                return;

            body.isKinematic = false;
        }

        static void SetKinematic(Rigidbody body)
        {
            if (body == null)
                return;

            ZeroVelocities(body);
            body.isKinematic = true;
        }

        void SetRagdollActive(bool active)
        {
            _isActive = active;
            if (!active)
                _knockdownTimer = 0f;

            if (_ragdollBodies == null)
                return;

            foreach (var body in _ragdollBodies)
            {
                if (body == null)
                    continue;

                if (active)
                    SetDynamic(body);
                else
                    SetKinematic(body);
            }

            if (!active)
                DisableHeavyPartialImmediate();
        }

        void EnableHeavyPartial(in HitContext hitContext)
        {
            DisableHeavyPartialImmediate();
            if (!HasRagdollSetup)
                return;

            SelectHeavyPartialBodies(hitContext.ContactPoint, _heavyPartialRadius, _heavyPartialMaxBodies, _heavyPartialBodies);
            if (_heavyPartialBodies.Count == 0)
                return;

            var worldDir = HitDirectionUtility.ResolveIncomingWorld(in hitContext, _transform);
            var impulse = ResolveImpulse(in hitContext);
            var force = worldDir.normalized * impulse;
            var primary = _heavyPartialBodies[0];
            var maxRadius = Mathf.Max(0.01f, _heavyPartialRadius);

            for (var i = 0; i < _heavyPartialBodies.Count; i++)
            {
                var body = _heavyPartialBodies[i];
                if (body == null)
                    continue;

                PrepareHeavyPartialBody(body);
                SetDynamic(body);
                body.WakeUp();

                if (i == 0)
                {
                    // 主受击骨使用可控倍率，避免局部关节被冲量瞬间拉开
                    // Use a controlled multiplier so local joints are not pulled apart by impulse spikes
                    body.AddForceAtPosition(force * _heavyPartialPrimaryImpulseMultiplier, hitContext.ContactPoint, ForceMode.Impulse);

                    var armVector = body.worldCenterOfMass - hitContext.ContactPoint;
                    var torqueAxis = Vector3.Cross(armVector, force);
                    if (torqueAxis.sqrMagnitude > 0.0001f)
                        body.AddTorque(torqueAxis.normalized * impulse * _heavyPartialAngularImpulseMultiplier, ForceMode.Impulse);
                }
                else
                {
                    var distance = Vector3.Distance(body.worldCenterOfMass, hitContext.ContactPoint);
                    var t = Mathf.Clamp01(distance / maxRadius);
                    var attenuatedScale = Mathf.Lerp(1f, 0.35f, t) * _heavyPartialSecondaryImpulseMultiplier;
                    body.AddForce(force * attenuatedScale, ForceMode.Impulse);
                }
            }

            _heavyPartialActive = true;
            _heavyPartialPhysicsActive = true;
            _heavyPartialBlendActive = false;
            _heavyPartialPhysicsElapsed = 0f;
            _heavyPartialBlendElapsed = 0f;
            _heavyPartialBlendWeight = 0f;

#if UNITY_EDITOR
            var bodyNames = string.Join(", ", _heavyPartialBodies.ConvertAll(b => b != null ? b.name : "null"));
            Debug.Log(
                $"[HeavyPartial] bodies={_heavyPartialBodies.Count}, contact={hitContext.ContactPoint}, impulse={impulse:F2}, selected=[{bodyNames}]",
                _transform);
#endif
        }

        void DisableHeavyPartialImmediate()
        {
            if (_heavyPartialBodies.Count > 0)
            {
                for (var i = 0; i < _heavyPartialBodies.Count; i++)
                {
                    var body = _heavyPartialBodies[i];
                    if (body == null)
                        continue;
                    RestoreHeavyPartialBodyState(body);
                    SetKinematic(body);
                }
                _heavyPartialBodies.Clear();
                _heavyPartialBodyStates.Clear();
            }

            _heavyPartialActive = false;
            _heavyPartialBlendActive = false;
            _heavyPartialPhysicsActive = false;
            _heavyPartialPhysicsElapsed = 0f;
            _heavyPartialBlendElapsed = 0f;
            _heavyPartialBlendWeight = 0f;
        }

        /// <summary>
        /// 重击局部链回收：动态窗口结束后只融合旋转回动画
        /// Heavy partial recovery — blend rotation back after the dynamic window
        /// </summary>
        public void ApplyHeavyPartialPoseLateUpdate()
        {
            if (!_heavyPartialActive || _heavyPartialBodies.Count == 0)
                return;

            for (var i = 0; i < _heavyPartialBodies.Count; i++)
            {
                var body = _heavyPartialBodies[i];
                if (body == null)
                    continue;

                if (_heavyPartialPhysicsActive)
                    continue;

                var stateIndex = FindHeavyPartialBodyStateIndex(body);
                if (stateIndex < 0)
                    continue;

                var bone = body.transform;
                var animationLocalRotation = bone.localRotation;
                var physicsLocalRotation = _heavyPartialBodyStates[stateIndex].PhysicsLocalRotation;

                if (_heavyPartialBlendActive)
                {
                    // 动态物理阶段结束后只融合局部旋转，避免继续写位置造成骨骼拉伸
                    // After dynamic physics, blend local rotation only to avoid positional stretching
                    bone.localRotation = Quaternion.Slerp(physicsLocalRotation, animationLocalRotation, _heavyPartialBlendWeight);
                }
                else
                {
                    bone.localRotation = physicsLocalRotation;
                }
            }
        }

        void TickHeavyPartialPhysicsHold(float deltaTime)
        {
            _heavyPartialPhysicsElapsed += Mathf.Max(0f, deltaTime);
            if (_heavyPartialPhysicsElapsed < _heavyPartialPhysicsHoldDuration)
                return;

            BeginHeavyPartialBlendBack();
        }

        void BeginHeavyPartialBlendBack()
        {
            for (var i = 0; i < _heavyPartialBodies.Count; i++)
            {
                var body = _heavyPartialBodies[i];
                if (body == null)
                    continue;

                var stateIndex = FindHeavyPartialBodyStateIndex(body);
                if (stateIndex >= 0)
                {
                    var state = _heavyPartialBodyStates[stateIndex];
                    state.PhysicsLocalRotation = body.transform.localRotation;
                    _heavyPartialBodyStates[stateIndex] = state;
                }

                RestoreHeavyPartialBodyState(body);
                SetKinematic(body);
            }

            _heavyPartialPhysicsActive = false;
            _heavyPartialBlendActive = true;
            _heavyPartialBlendElapsed = 0f;
            _heavyPartialBlendWeight = 0f;
        }

        void TickHeavyPartialBlendBack(float deltaTime)
        {
            _heavyPartialBlendElapsed += Mathf.Max(0f, deltaTime);
            var normalized = Mathf.Clamp01(_heavyPartialBlendElapsed / _heavyPartialBlendBackDuration);
            _heavyPartialBlendWeight = EvaluateHeavyPartialBlendWeight(normalized);

            if (normalized >= 1f || _heavyPartialBlendWeight >= 0.999f)
                CompleteHeavyPartialBlendBack();
        }

        float EvaluateHeavyPartialBlendWeight(float normalized)
        {
            if (_heavyPartialBlendBackCurve != null && _heavyPartialBlendBackCurve.length > 0)
                return Mathf.Clamp01(_heavyPartialBlendBackCurve.Evaluate(normalized));
            return Mathf.SmoothStep(0f, 1f, normalized);
        }

        void CompleteHeavyPartialBlendBack()
        {
            if (_heavyPartialBodies.Count > 0)
            {
                for (var i = 0; i < _heavyPartialBodies.Count; i++)
                {
                    var body = _heavyPartialBodies[i];
                    if (body == null)
                        continue;
                    RestoreHeavyPartialBodyState(body);
                    SetKinematic(body);
                }
                _heavyPartialBodies.Clear();
                _heavyPartialBodyStates.Clear();
            }

            _heavyPartialActive = false;
            _heavyPartialBlendActive = false;
            _heavyPartialBlendElapsed = 0f;
            _heavyPartialBlendWeight = 1f;
        }

        void SelectHeavyPartialBodies(Vector3 contactPoint, float radius, int maxBodies, List<Rigidbody> output)
        {
            output.Clear();
            if (_ragdollBodies == null || _ragdollBodies.Length == 0)
                return;

            var sortedByDistance = BuildBodiesSortedByDistance(contactPoint);
            if (sortedByDistance.Count == 0)
                return;

            var primary = sortedByDistance[0].body;
            AddBodyUnique(output, primary);
            if (primary == null)
                return;

            // 仅限制为受击部位及其子部位，不再扩展父链或邻近链
            // Strict scope: hit bone and its descendants only
            CollectChildrenWithinRadius(primary.transform, contactPoint, radius, output, maxBodies);
        }

        List<(Rigidbody body, float sqrDistance)> BuildBodiesSortedByDistance(Vector3 contactPoint)
        {
            var result = new List<(Rigidbody body, float sqrDistance)>(_ragdollBodies.Length);
            foreach (var body in _ragdollBodies)
            {
                if (body == null)
                    continue;
                var sqrDistance = (body.worldCenterOfMass - contactPoint).sqrMagnitude;
                result.Add((body, sqrDistance));
            }
            result.Sort((a, b) => a.sqrDistance.CompareTo(b.sqrDistance));
            return result;
        }

        void AddBodyUnique(List<Rigidbody> output, Rigidbody body)
        {
            if (body == null || output.Contains(body))
                return;
            output.Add(body);
        }

        void PrepareHeavyPartialBody(Rigidbody body)
        {
            if (body == null)
                return;

            if (FindHeavyPartialBodyStateIndex(body) < 0)
            {
                _heavyPartialBodyStates.Add(new HeavyPartialBodyState
                {
                    Body = body,
                    Interpolation = body.interpolation,
                    CollisionDetectionMode = body.collisionDetectionMode,
                    SolverIterations = body.solverIterations,
                    SolverVelocityIterations = body.solverVelocityIterations,
                    PhysicsLocalRotation = body.transform.localRotation
                });
            }

            // 局部 ragdoll 只短时动态，提高求解稳定性并降低高速穿透
            // Partial ragdoll is briefly dynamic; raise solver stability and reduce tunneling
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.solverIterations = _heavyPartialSolverIterations;
            body.solverVelocityIterations = _heavyPartialSolverVelocityIterations;
        }

        void RestoreHeavyPartialBodyState(Rigidbody body)
        {
            var stateIndex = FindHeavyPartialBodyStateIndex(body);
            if (stateIndex < 0)
                return;

            var state = _heavyPartialBodyStates[stateIndex];
            if (state.Body == null)
                return;

            state.Body.interpolation = state.Interpolation;
            state.Body.collisionDetectionMode = state.CollisionDetectionMode;
            state.Body.solverIterations = state.SolverIterations;
            state.Body.solverVelocityIterations = state.SolverVelocityIterations;
        }

        int FindHeavyPartialBodyStateIndex(Rigidbody body)
        {
            for (var i = 0; i < _heavyPartialBodyStates.Count; i++)
            {
                if (_heavyPartialBodyStates[i].Body == body)
                    return i;
            }

            return -1;
        }

        void CollectChildrenWithinRadius(
            Transform root,
            Vector3 contactPoint,
            float radius,
            List<Rigidbody> output,
            int maxBodies)
        {
            if (root == null || maxBodies <= 0 || output.Count >= maxBodies)
                return;

            var radiusSqr = Mathf.Max(0.01f, radius * radius);
            var candidates = new List<(Rigidbody body, float sqrDistance)>();
            foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
            {
                if (rb == null || rb.transform == root)
                    continue;
                if (Array.IndexOf(_ragdollBodies, rb) < 0)
                    continue;
                var sqrDistance = (rb.worldCenterOfMass - contactPoint).sqrMagnitude;
                if (sqrDistance > radiusSqr)
                    continue;
                candidates.Add((rb, sqrDistance));
            }

            candidates.Sort((a, b) => a.sqrDistance.CompareTo(b.sqrDistance));
            for (var i = 0; i < candidates.Count && output.Count < maxBodies; i++)
                AddBodyUnique(output, candidates[i].body);
        }

        public CharacterPoseSnapshot CaptureRecoveryPoseSnapshot()
        {
            if (!HasRagdollSetup)
                return default;

            var bones = new Transform[_ragdollBodies.Length];
            var localRotations = new Quaternion[_ragdollBodies.Length];
            var count = 0;

            foreach (var body in _ragdollBodies)
            {
                if (body == null)
                    continue;

                bones[count] = body.transform;
                localRotations[count] = body.transform.localRotation;
                count++;
            }

            if (count == 0)
                return default;

            if (count != bones.Length)
            {
                Array.Resize(ref bones, count);
                Array.Resize(ref localRotations, count);
            }

            return new CharacterPoseSnapshot(bones, localRotations);
        }

        public RagdollAnchor CaptureRecoveryAnchor()
        {
            if (!HasRagdollSetup)
                return new RagdollAnchor(Vector3.zero, _transform.forward, false);

            var hips = FindBodyByNameContains("hip");
            if (hips == null)
                hips = _ragdollBodies[0];
            if (hips == null)
                return new RagdollAnchor(Vector3.zero, _transform.forward, false);

            var hipsPos = hips.position;
            var chest = FindBodyByNameContains("chest") ?? FindBodyByNameContains("spine");
            var planarForward = _transform.forward;

            if (chest != null)
            {
                var toChest = chest.position - hipsPos;
                toChest.y = 0f;
                if (toChest.sqrMagnitude > 0.0001f)
                    planarForward = toChest.normalized;
            }

            planarForward.y = 0f;
            if (planarForward.sqrMagnitude < 0.0001f)
                planarForward = _transform.forward;

            return new RagdollAnchor(hipsPos, planarForward.normalized, true);
        }

        Rigidbody FindBodyByNameContains(string keyword)
        {
            if (_ragdollBodies == null)
                return null;

            for (var i = 0; i < _ragdollBodies.Length; i++)
            {
                var body = _ragdollBodies[i];
                if (body == null)
                    continue;
                if (body.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return body;
            }

            return null;
        }
    }
}
