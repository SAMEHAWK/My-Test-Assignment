using System.Collections.Generic;
using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 武器攻击命中驱动：动画事件开启窗口，按剑刃轨迹扫描 Hurtbox
    /// Weapon attack hit driver: animation events open windows and scan hurtboxes along blade trail
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponAttackHitDriver : MonoBehaviour
    {
        struct TrailSample
        {
            public Vector3 PreviousStart;
            public Vector3 PreviousEnd;
            public Vector3 CurrentStart;
            public Vector3 CurrentEnd;
            public CharacterAttackType AttackType;
            public float Time;
        }

        struct HitSample
        {
            public Vector3 Point;
            public float Time;
        }

        [Header("Owner / 所属角色")]
        [SerializeField] CharacterControllerRoot owner;
        [SerializeField] Transform bladeStart;
        [SerializeField] Transform bladeEnd;

        [Header("Hit Scan / 命中扫描")]
        [SerializeField] LayerMask hurtboxMask = ~0;
        [SerializeField] QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Collide;
        [SerializeField] float lightAttackRadius = 0.08f;
        [SerializeField] float heavyAttackRadius = 0.12f;
        [SerializeField] float lightImpulse = 2f;
        [SerializeField] float heavyImpulse = 6f;
        [SerializeField] bool allowMultipleRegionsPerTarget;

        [Header("Gizmos / 轨迹可视化")]
        [SerializeField] bool drawGizmos = true;
        [SerializeField] bool drawOnlyWhenSelected = true;
        [SerializeField] float gizmoDuration = 1.25f;
        [SerializeField] int maxTrailSamples = 32;
        [SerializeField] float hitPointRadius = 0.04f;

        readonly HashSet<CharacterControllerRoot> _hitOwners = new();
        readonly HashSet<CharacterHurtbox> _hitHurtboxes = new();
        readonly List<TrailSample> _trailSamples = new();
        readonly List<HitSample> _hitSamples = new();

        bool _hitWindowActive;
        bool _hasPreviousBlade;
        CharacterAttackType _currentAttackType;
        Vector3 _previousBladeStart;
        Vector3 _previousBladeEnd;

        void Awake()
        {
            ResolveOwnerIfMissing();
        }

        void OnValidate()
        {
            ResolveOwnerIfMissing();
            lightAttackRadius = Mathf.Max(0.001f, lightAttackRadius);
            heavyAttackRadius = Mathf.Max(0.001f, heavyAttackRadius);
            lightImpulse = Mathf.Max(0f, lightImpulse);
            heavyImpulse = Mathf.Max(0f, heavyImpulse);
            maxTrailSamples = Mathf.Max(1, maxTrailSamples);
            hitPointRadius = Mathf.Max(0.001f, hitPointRadius);
        }

        void Update()
        {
            if (!_hitWindowActive)
                return;
            if (!TryReadBlade(out var currentStart, out var currentEnd))
                return;

            if (!_hasPreviousBlade)
            {
                CachePreviousBlade(currentStart, currentEnd);
                return;
            }

            RecordTrailSample(_previousBladeStart, _previousBladeEnd, currentStart, currentEnd);
            ScanBladeMotion(_previousBladeStart, _previousBladeEnd, currentStart, currentEnd);
            CachePreviousBlade(currentStart, currentEnd);
        }

        public void BeginLightAttackHitWindow() => BeginHitWindow(CharacterAttackType.Light);

        public void BeginHeavyAttackHitWindow() => BeginHitWindow(CharacterAttackType.Heavy);

        public void EndAttackHitWindow()
        {
            _hitWindowActive = false;
            _hasPreviousBlade = false;
        }

        void BeginHitWindow(CharacterAttackType attackType)
        {
            _currentAttackType = attackType;
            _hitWindowActive = true;
            _hasPreviousBlade = false;
            _hitOwners.Clear();
            _hitHurtboxes.Clear();

            if (TryReadBlade(out var currentStart, out var currentEnd))
                CachePreviousBlade(currentStart, currentEnd);
        }

        void ScanBladeMotion(Vector3 previousStart, Vector3 previousEnd, Vector3 currentStart, Vector3 currentEnd)
        {
            var previousMid = (previousStart + previousEnd) * 0.5f;
            var currentMid = (currentStart + currentEnd) * 0.5f;
            var radius = ResolveCurrentRadius();

            SphereCastSegment(previousStart, currentStart, radius);
            SphereCastSegment(previousEnd, currentEnd, radius);
            SphereCastSegment(previousMid, currentMid, radius);
            OverlapCurrentBlade(currentStart, currentEnd, currentMid - previousMid, radius);
        }

        void SphereCastSegment(Vector3 from, Vector3 to, float radius)
        {
            var delta = to - from;
            var distance = delta.magnitude;
            if (distance <= 0.0001f)
            {
                OverlapSpherePoint(to, radius);
                return;
            }

            var hits = Physics.SphereCastAll(
                from,
                radius,
                delta / distance,
                distance,
                hurtboxMask,
                triggerInteraction);

            for (var i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                TryApplyHit(hit.collider, hit.point, delta);
            }
        }

        void OverlapCurrentBlade(Vector3 currentStart, Vector3 currentEnd, Vector3 bladeMotion, float radius)
        {
            var colliders = Physics.OverlapCapsule(
                currentStart,
                currentEnd,
                radius,
                hurtboxMask,
                triggerInteraction);
            var bladeMid = (currentStart + currentEnd) * 0.5f;

            for (var i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                var point = collider != null ? collider.ClosestPoint(bladeMid) : bladeMid;
                TryApplyHit(collider, point, bladeMotion);
            }
        }

        void OverlapSpherePoint(Vector3 point, float radius)
        {
            var colliders = Physics.OverlapSphere(
                point,
                radius,
                hurtboxMask,
                triggerInteraction);

            for (var i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                var contact = collider != null ? collider.ClosestPoint(point) : point;
                TryApplyHit(collider, contact, transform.forward);
            }
        }

        void TryApplyHit(Collider hitCollider, Vector3 contactPoint, Vector3 fallbackMotion)
        {
            if (hitCollider == null)
                return;

            var hurtbox = hitCollider.GetComponentInParent<CharacterHurtbox>();
            if (hurtbox == null || hurtbox.Owner == null)
                return;
            if (owner != null && hurtbox.Owner == owner)
                return;
            if (!allowMultipleRegionsPerTarget && !_hitOwners.Add(hurtbox.Owner))
                return;
            if (allowMultipleRegionsPerTarget && !_hitHurtboxes.Add(hurtbox))
                return;

            var attackDirection = ResolveAttackDirection(fallbackMotion, hurtbox.Owner.transform);
            var sourceDirection = ResolveSourceDirection(hurtbox.Owner.transform);
            var hitType = _currentAttackType == CharacterAttackType.Heavy ? HitType.Heavy : HitType.Light;
            var impulse = ResolveCurrentImpulse() * hurtbox.ImpulseMultiplier;
            var hitDirection = HitDirectionUtility.ResolveDirectionFromWorld(attackDirection, hurtbox.Owner.transform);

            var context = new HitContext(
                hitType,
                hitDirection,
                contactPoint,
                impulse,
                source: owner != null ? owner : this,
                worldIncomingDirection: attackDirection,
                worldSourceDirection: sourceDirection);

            RecordHitPoint(contactPoint);
            hurtbox.Owner.ReceiveHit(context);
        }

        Vector3 ResolveAttackDirection(Vector3 fallbackMotion, Transform target)
        {
            var direction = fallbackMotion;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
                return direction.normalized;

            if (target != null)
            {
                var sourcePosition = owner != null ? owner.transform.position : transform.position;
                direction = target.position - sourcePosition;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.0001f)
                    return direction.normalized;
            }

            direction = owner != null ? owner.transform.forward : transform.forward;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        }

        Vector3 ResolveSourceDirection(Transform target)
        {
            if (target != null)
            {
                var sourcePosition = owner != null ? owner.transform.position : transform.position;
                var direction = target.position - sourcePosition;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.0001f)
                    return direction.normalized;
            }

            var fallback = owner != null ? owner.transform.forward : transform.forward;
            fallback.y = 0f;
            return fallback.sqrMagnitude > 0.0001f ? fallback.normalized : Vector3.forward;
        }

        bool TryReadBlade(out Vector3 start, out Vector3 end)
        {
            start = bladeStart != null ? bladeStart.position : transform.position;
            end = bladeEnd != null ? bladeEnd.position : start;
            return bladeStart != null && bladeEnd != null;
        }

        void CachePreviousBlade(Vector3 start, Vector3 end)
        {
            _previousBladeStart = start;
            _previousBladeEnd = end;
            _hasPreviousBlade = true;
        }

        float ResolveCurrentRadius() =>
            _currentAttackType == CharacterAttackType.Heavy
                ? Mathf.Max(0.001f, heavyAttackRadius)
                : Mathf.Max(0.001f, lightAttackRadius);

        float ResolveCurrentImpulse() =>
            _currentAttackType == CharacterAttackType.Heavy
                ? Mathf.Max(0f, heavyImpulse)
                : Mathf.Max(0f, lightImpulse);

        void ResolveOwnerIfMissing()
        {
            if (owner != null)
                return;

            owner = GetComponentInParent<CharacterControllerRoot>();
        }

        void RecordTrailSample(Vector3 previousStart, Vector3 previousEnd, Vector3 currentStart, Vector3 currentEnd)
        {
            _trailSamples.Add(new TrailSample
            {
                PreviousStart = previousStart,
                PreviousEnd = previousEnd,
                CurrentStart = currentStart,
                CurrentEnd = currentEnd,
                AttackType = _currentAttackType,
                Time = Time.time
            });

            TrimTrailSamples();
        }

        void RecordHitPoint(Vector3 point)
        {
            _hitSamples.Add(new HitSample
            {
                Point = point,
                Time = Time.time
            });
        }

        void TrimTrailSamples()
        {
            var now = Time.time;
            for (var i = _trailSamples.Count - 1; i >= 0; i--)
            {
                if (_trailSamples.Count <= maxTrailSamples && now - _trailSamples[i].Time <= gizmoDuration)
                    continue;
                _trailSamples.RemoveAt(i);
            }

            for (var i = _hitSamples.Count - 1; i >= 0; i--)
            {
                if (now - _hitSamples[i].Time <= gizmoDuration)
                    continue;
                _hitSamples.RemoveAt(i);
            }
        }

        void OnDrawGizmos()
        {
            if (!drawGizmos || drawOnlyWhenSelected)
                return;

            DrawAttackGizmos();
        }

        void OnDrawGizmosSelected()
        {
            if (!drawGizmos)
                return;

            DrawAttackGizmos();
        }

        void DrawAttackGizmos()
        {
            TrimTrailSamples();
            DrawCurrentBladeGizmo();
            DrawTrailGizmos();
            DrawHitGizmos();
        }

        void DrawCurrentBladeGizmo()
        {
            if (bladeStart == null || bladeEnd == null)
                return;

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(bladeStart.position, bladeEnd.position);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(bladeStart.position, Mathf.Max(0.001f, lightAttackRadius));
            Gizmos.DrawWireSphere(bladeEnd.position, Mathf.Max(0.001f, lightAttackRadius));

            Gizmos.color = new Color(1f, 0.45f, 0f);
            Gizmos.DrawWireSphere(bladeStart.position, Mathf.Max(0.001f, heavyAttackRadius));
            Gizmos.DrawWireSphere(bladeEnd.position, Mathf.Max(0.001f, heavyAttackRadius));
        }

        void DrawTrailGizmos()
        {
            for (var i = 0; i < _trailSamples.Count; i++)
            {
                var sample = _trailSamples[i];
                Gizmos.color = sample.AttackType == CharacterAttackType.Heavy
                    ? new Color(1f, 0.45f, 0f)
                    : Color.yellow;
                Gizmos.DrawLine(sample.PreviousStart, sample.CurrentStart);
                Gizmos.DrawLine(sample.PreviousEnd, sample.CurrentEnd);
                Gizmos.DrawLine(sample.CurrentStart, sample.CurrentEnd);
            }
        }

        void DrawHitGizmos()
        {
            Gizmos.color = Color.red;
            for (var i = 0; i < _hitSamples.Count; i++)
                Gizmos.DrawSphere(_hitSamples[i].Point, hitPointRadius);
        }
    }
}
