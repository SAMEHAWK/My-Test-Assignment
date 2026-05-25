using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 俯视角平面移动（Unity CharacterController），速度标量线性加减速
    /// Top-down planar movement with linear speed ramp-up/down
    /// </summary>
    public sealed class CharacterMotor : ICharacterModule
    {
        readonly Transform _moveTransform;
        readonly Transform _rotationTransform;
        readonly UnityEngine.CharacterController _controller;
        readonly CharacterControllerConfig _config;

        Vector2 _moveInput;
        Vector3 _worldVelocity;
        Vector3 _lastMoveDirection = Vector3.forward;
        float _currentPlanarSpeed;
        float _verticalVelocity;

        public Vector3 WorldVelocity => _worldVelocity;

        public CharacterMotor(
            Transform moveTransform,
            UnityEngine.CharacterController controller,
            CharacterControllerConfig config,
            Transform rotationTransform = null)
        {
            _moveTransform = moveTransform;
            _rotationTransform = rotationTransform != null ? rotationTransform : moveTransform;
            _controller = controller;
            _config = config;
        }

        /// <summary>
        /// 由 Root / Input 设置移动意图
        /// Set move intent from root or input reader
        /// </summary>
        public void SetMoveInput(Vector2 input)
        {
            _moveInput = Vector2.ClampMagnitude(input, 1f);
        }

        public void TickMovement(float deltaTime, bool allowsInput, CharacterState state, bool weaponEquipped)
        {
            if (_controller == null || _config == null)
            {
                _worldVelocity = Vector3.zero;
                return;
            }
            if (!_controller.enabled)
            {
                _currentPlanarSpeed = 0f;
                _verticalVelocity = 0f;
                _worldVelocity = Vector3.zero;
                return;
            }

            var verticalDelta = TickVerticalMotion(deltaTime);

            if (!allowsInput)
            {
                _currentPlanarSpeed = 0f;
                _worldVelocity = Vector3.zero;
                MoveController(verticalDelta);
                return;
            }

            var maxSpeed = _config.GetMaxMoveSpeed(weaponEquipped);
            if (state == CharacterState.HeavyStagger)
                maxSpeed *= _config.heavyMoveSpeedMultiplier;

            var input = new Vector3(_moveInput.x, 0f, _moveInput.y);
            var hasInput = input.sqrMagnitude > 0.0001f;

            var targetSpeed = hasInput ? maxSpeed : 0f;
            var rate = targetSpeed >= _currentPlanarSpeed
                ? _config.moveAcceleration
                : _config.moveDeceleration;

            _currentPlanarSpeed = Mathf.MoveTowards(
                _currentPlanarSpeed,
                targetSpeed,
                rate * deltaTime);

            if (hasInput)
            {
                _lastMoveDirection = input.normalized;

                if (_config.rotationSpeed > 0f)
                {
                    var targetRotation = Quaternion.LookRotation(_lastMoveDirection, Vector3.up);
                    _rotationTransform.rotation = Quaternion.Slerp(
                        _rotationTransform.rotation,
                        targetRotation,
                        _config.rotationSpeed * deltaTime);
                }
            }

            if (_currentPlanarSpeed < 0.001f)
            {
                _currentPlanarSpeed = 0f;
                _worldVelocity = Vector3.zero;
                MoveController(verticalDelta);
                return;
            }

            _worldVelocity = _lastMoveDirection * _currentPlanarSpeed;
            MoveController(_worldVelocity * deltaTime + verticalDelta);
        }

        public void OnEnterState(CharacterState state, in HitContext hitContext)
        {
            if (_controller == null)
                return;

            if (state is CharacterState.HeavyStagger
                or CharacterState.AttackPlayback
                or CharacterState.Knockdown or CharacterState.ForcedKnockdown
                or CharacterState.Recovering)
            {
                _currentPlanarSpeed = 0f;
                _verticalVelocity = 0f;
                _worldVelocity = Vector3.zero;
            }

            _controller.enabled = state is CharacterState.Locomotion
                or CharacterState.WeaponEquipPlayback
                or CharacterState.LightFlinch
                // 重击/攻击播片期间需保持 CC 开启，供 Root Motion 回写根节点位移
                // Keep CC enabled during authored playback so root-motion can drive root displacement
                or CharacterState.HeavyStagger
                or CharacterState.AttackPlayback;
        }

        public void OnExitState(CharacterState state) { }

        public void OnTickState(CharacterState state, float deltaTime) { }

        public void OnFixedTickState(CharacterState state, float fixedDeltaTime) { }

        /// <summary>
        /// 轻击：根节点沿受击反方向瞬时位移（不依赖 Ragdoll/Animator）
        /// Light hit — instant root knockback via CharacterController
        /// </summary>
        public void ApplyLightHitRootKnockback(in HitContext hitContext, Transform facingTransform)
        {
            if (_controller == null || _config == null)
                return;

            var distance = _config.lightFlinchRootKnockbackDistance;
            if (distance <= 0f)
                return;

            var incoming = HitDirectionUtility.ResolveIncomingWorld(in hitContext, facingTransform);
            incoming.y = 0f;
            if (incoming.sqrMagnitude < 0.0001f)
                incoming = facingTransform.forward;

            var recoil = -incoming.normalized * distance;
            _controller.Move(recoil);
        }

        Vector3 TickVerticalMotion(float deltaTime)
        {
            if (_controller == null || _config == null)
                return Vector3.zero;

            if (_controller.isGrounded && _verticalVelocity < 0f)
            {
                // 接地时保留轻微下压力，抑制 CC 被碰撞解穿透逐步抬高
                // Keep a small downward stick velocity to reduce upward depenetration drift
                _verticalVelocity = Mathf.Min(0f, _config.groundedStickVelocity);
            }
            else
            {
                _verticalVelocity += _config.gravity * deltaTime;
                var maxFall = Mathf.Max(0f, _config.maxFallSpeed);
                if (maxFall > 0f)
                    _verticalVelocity = Mathf.Max(_verticalVelocity, -maxFall);
            }

            return Vector3.up * (_verticalVelocity * deltaTime);
        }

        void MoveController(Vector3 delta)
        {
            if (delta.sqrMagnitude <= 0.0000001f)
                return;

            _controller.Move(delta);
        }
    }
}
