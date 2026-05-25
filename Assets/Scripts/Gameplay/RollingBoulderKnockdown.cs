using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 沿固定两点轨迹往返滚动的击倒球
    /// Rolling boulder that moves between two points and force-knocks characters down
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RollingBoulderKnockdown : MonoBehaviour
    {
        [Header("Path / 轨迹")]
        [SerializeField] Transform pointA;
        [SerializeField] Transform pointB;
        [SerializeField] float moveSpeed = 3f;
        [SerializeField] float endpointWaitSeconds = 1f;
        [SerializeField] bool startTowardB = true;

        [Header("Hit / 命中")]
        [SerializeField] float impulse = 8f;
        [SerializeField] bool hitOnlyWhileMoving = true;
        [SerializeField] float sameTargetHitCooldown = 1f;
        [SerializeField] LayerMask characterMask = ~0;
        [SerializeField] QueryTriggerInteraction characterTriggerInteraction = QueryTriggerInteraction.Ignore;
        [Tooltip("主动扫描角色半径；<=0 时按 Collider bounds 自动估算 — Active character scan radius; <=0 uses collider bounds")]
        [SerializeField] float contactScanRadius = -1f;

        [Header("Rolling Visual / 滚动表现")]
        [SerializeField] bool rotateVisual = true;
        [SerializeField] float visualRadius = 0.5f;
        [SerializeField] bool forceTransformSync = true;

        Rigidbody _rigidbody;
        Vector3 _targetPosition;
        Vector3 _lastMoveDirection = Vector3.forward;
        bool _movingTowardB;
        bool _waiting;
        float _waitTimer;
        CharacterControllerRoot _lastHitTarget;
        float _lastHitTime = -999f;

        void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _rigidbody.isKinematic = true;
            _rigidbody.useGravity = false;
            _movingTowardB = startTowardB;
            SnapToStartPointIfAvailable();
            RefreshTargetPosition();
        }

        void OnValidate()
        {
            moveSpeed = Mathf.Max(0f, moveSpeed);
            endpointWaitSeconds = Mathf.Max(0f, endpointWaitSeconds);
            impulse = Mathf.Max(0f, impulse);
            sameTargetHitCooldown = Mathf.Max(0f, sameTargetHitCooldown);
            visualRadius = Mathf.Max(0.001f, visualRadius);
            contactScanRadius = Mathf.Max(-1f, contactScanRadius);
        }

        void FixedUpdate()
        {
            if (pointA == null || pointB == null || _rigidbody == null)
                return;

            if (_waiting)
            {
                TickEndpointWait();
                return;
            }

            MoveAlongPath();
        }

        void OnCollisionEnter(Collision collision)
        {
            if (collision == null || collision.collider == null)
                return;

            var contactPoint = collision.contactCount > 0
                ? collision.GetContact(0).point
                : collision.collider.ClosestPoint(transform.position);
            TryForceKnockdown(collision.collider, contactPoint);
        }

        void OnTriggerEnter(Collider other)
        {
            if (other == null)
                return;

            TryForceKnockdown(other, other.ClosestPoint(transform.position));
        }

        void MoveAlongPath()
        {
            var current = _rigidbody.position;
            var toTarget = _targetPosition - current;
            var distance = toTarget.magnitude;
            if (distance <= 0.001f || moveSpeed <= 0f)
            {
                BeginEndpointWait();
                return;
            }

            var direction = toTarget / distance;
            var step = Mathf.Min(moveSpeed * Time.fixedDeltaTime, distance);
            var nextPosition = current + direction * step;

            _lastMoveDirection = direction;
            MoveBoulder(nextPosition);
            ApplyRollingRotation(direction, step);
            ScanCharactersAtCurrentPosition();

            if (step >= distance - 0.001f)
                BeginEndpointWait();
        }

        void TickEndpointWait()
        {
            _waitTimer += Time.fixedDeltaTime;
            if (_waitTimer < endpointWaitSeconds)
                return;

            _waiting = false;
            _waitTimer = 0f;
            _movingTowardB = !_movingTowardB;
            RefreshTargetPosition();
        }

        void BeginEndpointWait()
        {
            _waiting = true;
            _waitTimer = 0f;
            MoveBoulder(_targetPosition);
        }

        void RefreshTargetPosition()
        {
            if (pointA == null || pointB == null)
                return;

            _targetPosition = _movingTowardB ? pointB.position : pointA.position;
        }

        void SnapToStartPointIfAvailable()
        {
            var start = startTowardB ? pointA : pointB;
            if (start == null)
                return;

            SetBoulderPositionImmediate(start.position);
        }

        void ApplyRollingRotation(Vector3 direction, float distance)
        {
            if (!rotateVisual || distance <= 0f)
                return;

            var axis = Vector3.Cross(Vector3.up, direction);
            if (axis.sqrMagnitude < 0.0001f)
                return;

            var degrees = distance / visualRadius * Mathf.Rad2Deg;
            var nextRotation = Quaternion.AngleAxis(degrees, axis.normalized) * _rigidbody.rotation;
            _rigidbody.MoveRotation(nextRotation);
            if (forceTransformSync)
                transform.rotation = nextRotation;
        }

        void MoveBoulder(Vector3 position)
        {
            _rigidbody.MovePosition(position);
            if (forceTransformSync)
                transform.position = position;
        }

        void SetBoulderPositionImmediate(Vector3 position)
        {
            _rigidbody.position = position;
            transform.position = position;
        }

        void TryForceKnockdown(Collider hitCollider, Vector3 contactPoint)
        {
            if (hitOnlyWhileMoving && _waiting)
                return;

            var target = hitCollider.GetComponentInParent<CharacterControllerRoot>();
            if (target == null)
                return;

            if (_lastHitTarget == target && Time.time - _lastHitTime < sameTargetHitCooldown)
                return;

            var direction = _lastMoveDirection;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
                direction = transform.forward;
            direction.Normalize();

            var hitDirection = HitDirectionUtility.ResolveDirectionFromWorld(direction, target.transform);
            var hitContext = new HitContext(
                HitType.ForceKnockdownHeavy,
                hitDirection,
                contactPoint,
                impulse,
                bypassBalance: true,
                source: this,
                worldIncomingDirection: direction);

            _lastHitTarget = target;
            _lastHitTime = Time.time;
            target.ReceiveHit(hitContext);
        }

        void ScanCharactersAtCurrentPosition()
        {
            if (hitOnlyWhileMoving && _waiting)
                return;

            // CharacterController 不一定会触发球体的 OnCollisionEnter，主动扫描可确保“推到角色”也算命中
            // CharacterController may not invoke OnCollisionEnter on the boulder, so active scan catches pushed contacts
            var radius = ResolveContactScanRadius();
            var colliders = Physics.OverlapSphere(
                transform.position,
                radius,
                characterMask,
                characterTriggerInteraction);

            for (var i = 0; i < colliders.Length; i++)
            {
                var hitCollider = colliders[i];
                if (hitCollider == null)
                    continue;

                var contactPoint = hitCollider.ClosestPoint(transform.position);
                TryForceKnockdown(hitCollider, contactPoint);
            }
        }

        float ResolveContactScanRadius()
        {
            if (contactScanRadius > 0f)
                return contactScanRadius;

            var collider = GetComponent<Collider>();
            if (collider == null)
                return Mathf.Max(0.001f, visualRadius);

            var extents = collider.bounds.extents;
            return Mathf.Max(0.001f, Mathf.Max(extents.x, Mathf.Max(extents.y, extents.z)));
        }

        void OnDrawGizmosSelected()
        {
            if (pointA == null || pointB == null)
                return;

            Gizmos.color = Color.gray;
            Gizmos.DrawLine(pointA.position, pointB.position);
            Gizmos.DrawWireSphere(pointA.position, 0.12f);
            Gizmos.DrawWireSphere(pointB.position, 0.12f);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, contactScanRadius > 0f ? contactScanRadius : visualRadius);
        }
    }
}
