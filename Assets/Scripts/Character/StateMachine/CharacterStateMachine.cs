using System;
using System.Collections.Generic;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 角色玩法层次状态机：子态、父态、能力查询与受控迁移
    /// Character gameplay HSM — substates, superstates, capabilities, transitions
    /// </summary>
    public sealed class CharacterStateMachine
    {
        CharacterState _current = CharacterState.Locomotion;
        float _timeInState;

        static readonly HashSet<(CharacterState From, CharacterState To)> AllowedTransitions = BuildAllowedTransitions();

        public CharacterState CurrentState => _current;

        public float TimeInState => _timeInState;

        public CharacterStateCapabilities Capabilities => CharacterStateCapabilities.For(_current);

        public event Action<CharacterState, CharacterState> StateChanged;

        public void ResetToLocomotion()
        {
            _current = CharacterState.Locomotion;
            _timeInState = 0f;
        }

        public void Tick(float deltaTime) => _timeInState += deltaTime;

        /// <summary>
        /// 尝试迁移；非法迁移返回 false
        /// Try transition; returns false if not allowed
        /// </summary>
        public bool TryTransition(CharacterState target, out string reason)
        {
            reason = null;

            if (target == _current && target != CharacterState.Locomotion)
            {
                reason = "Already in state";
                return false;
            }

            if (_current == target)
            {
                _timeInState = 0f;
                return true;
            }

            if (!IsTransitionAllowed(_current, target))
            {
                reason = $"Transition {_current} -> {target} not allowed";
                return false;
            }

            var old = _current;
            _current = target;
            _timeInState = 0f;
            StateChanged?.Invoke(old, target);
            return true;
        }

        /// <summary>
        /// 受击导致的迁移目标（不含 Balance 判定，由 Root 先扣点）
        /// Resolve hit reaction target state from hit type
        /// </summary>
        public static CharacterState ResolveHitTarget(in HitContext hitContext, bool balanceDepleted)
        {
            if (hitContext.BypassBalance
                || hitContext.Type == HitType.ForceKnockdownLight
                || hitContext.Type == HitType.ForceKnockdownHeavy)
                return CharacterState.ForcedKnockdown;

            if (balanceDepleted)
                return CharacterState.Knockdown;

            return hitContext.Type switch
            {
                HitType.Light => CharacterState.LightFlinch,
                HitType.Heavy => CharacterState.HeavyStagger,
                HitType.ForceKnockdownLight => CharacterState.ForcedKnockdown,
                HitType.ForceKnockdownHeavy => CharacterState.ForcedKnockdown,
                _ => CharacterState.Locomotion
            };
        }

        /// <summary>
        /// 受击时是否应先中止武器 overlay（Grounded 且正在播片）
        /// Whether weapon overlay should abort before leaving grounded playback
        /// </summary>
        public bool ShouldAbortWeaponOnTransition(CharacterState target)
        {
            if (_current != CharacterState.WeaponEquipPlayback)
                return false;

            return target is CharacterState.HeavyStagger
                or CharacterState.Knockdown
                or CharacterState.ForcedKnockdown;
        }

        static bool IsTransitionAllowed(CharacterState from, CharacterState to) =>
            AllowedTransitions.Contains((from, to));

        static HashSet<(CharacterState, CharacterState)> BuildAllowedTransitions()
        {
            var set = new HashSet<(CharacterState, CharacterState)>();

            void Allow(CharacterState from, CharacterState to) => set.Add((from, to));

            // Grounded
            Allow(CharacterState.Locomotion, CharacterState.WeaponEquipPlayback);
            Allow(CharacterState.WeaponEquipPlayback, CharacterState.Locomotion);

            // Hits from grounded
            Allow(CharacterState.Locomotion, CharacterState.LightFlinch);
            Allow(CharacterState.Locomotion, CharacterState.HeavyStagger);
            Allow(CharacterState.Locomotion, CharacterState.Knockdown);
            Allow(CharacterState.Locomotion, CharacterState.ForcedKnockdown);

            Allow(CharacterState.WeaponEquipPlayback, CharacterState.LightFlinch);
            Allow(CharacterState.WeaponEquipPlayback, CharacterState.HeavyStagger);
            Allow(CharacterState.WeaponEquipPlayback, CharacterState.Knockdown);
            Allow(CharacterState.WeaponEquipPlayback, CharacterState.ForcedKnockdown);

            // Hit reaction return / escalate
            Allow(CharacterState.LightFlinch, CharacterState.Locomotion);
            Allow(CharacterState.HeavyStagger, CharacterState.Locomotion);
            Allow(CharacterState.LightFlinch, CharacterState.Knockdown);
            Allow(CharacterState.LightFlinch, CharacterState.ForcedKnockdown);
            Allow(CharacterState.HeavyStagger, CharacterState.Knockdown);
            Allow(CharacterState.HeavyStagger, CharacterState.ForcedKnockdown);

            // Incapacitated chain
            Allow(CharacterState.Knockdown, CharacterState.Recovering);
            Allow(CharacterState.ForcedKnockdown, CharacterState.Recovering);
            Allow(CharacterState.Recovering, CharacterState.Locomotion);

            // Re-enter locomotion refreshes timer
            Allow(CharacterState.Locomotion, CharacterState.Locomotion);

            return set;
        }
    }
}
