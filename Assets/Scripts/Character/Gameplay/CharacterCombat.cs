using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 平衡值：扣减、恢复、击倒判定
    /// Balance: damage, regeneration, knockdown threshold
    /// </summary>
    public sealed class CharacterCombat : ICharacterModule
    {
        readonly CharacterControllerConfig _config;

        int _currentBalance;
        float _timeSinceLastHit;

        public int CurrentBalance => _currentBalance;
        public int MaxBalance => _config != null ? _config.maxBalance : 6;

        public CharacterCombat(CharacterControllerConfig config)
        {
            _config = config;
            ResetBalance();
        }

        public void ResetBalance()
        {
            _currentBalance = _config != null ? _config.maxBalance : 6;
            _timeSinceLastHit = 0f;
        }

        public bool ApplyBalanceDamage(in HitContext hitContext)
        {
            if (_config == null)
                return false;

            _timeSinceLastHit = 0f;

            var damage = hitContext.Type == HitType.Heavy
                ? _config.heavyBalanceDamage
                : _config.lightBalanceDamage;

            _currentBalance = Mathf.Max(0, _currentBalance - damage);
            return _currentBalance <= 0;
        }

        /// <summary>
        /// 强制清空平衡值（用于强制击倒调试/特殊攻击）
        /// Force-deplete balance (for forced knockdown debug/special attacks)
        /// </summary>
        public void ForceDepleteBalance()
        {
            _currentBalance = 0;
            _timeSinceLastHit = 0f;
        }

        public void TickBalance(float deltaTime)
        {
            if (_config == null)
                return;

            _timeSinceLastHit += deltaTime;
            if (_timeSinceLastHit < _config.balanceRegenDelay)
                return;

            if (_currentBalance >= _config.maxBalance)
                return;

            _currentBalance = Mathf.Min(
                _config.maxBalance,
                _currentBalance + Mathf.RoundToInt(_config.balanceRegenPerSecond * deltaTime));
        }

        public void OnEnterState(CharacterState state, in HitContext hitContext) { }

        public void OnExitState(CharacterState state) { }

        public void OnTickState(CharacterState state, float deltaTime) { }

        public void OnFixedTickState(CharacterState state, float fixedDeltaTime) { }
    }
}
