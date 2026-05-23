namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 当前子态对外能力（移动、拔刀等）
    /// Per-state capabilities exposed to Root and input
    /// </summary>
    public readonly struct CharacterStateCapabilities
    {
        public CharacterState State { get; }
        public CharacterSuperstate Superstate { get; }
        public bool CanMove { get; }
        public bool CanToggleWeapon { get; }
        public bool CanReceiveHit { get; }

        public CharacterStateCapabilities(
            CharacterState state,
            CharacterSuperstate superstate,
            bool canMove,
            bool canToggleWeapon,
            bool canReceiveHit = true)
        {
            State = state;
            Superstate = superstate;
            CanMove = canMove;
            CanToggleWeapon = canToggleWeapon;
            CanReceiveHit = canReceiveHit;
        }

        public static CharacterStateCapabilities For(CharacterState state)
        {
            return state switch
            {
                CharacterState.Locomotion => new CharacterStateCapabilities(
                    state,
                    CharacterSuperstate.Grounded,
                    canMove: true,
                    canToggleWeapon: true),

                CharacterState.WeaponEquipPlayback => new CharacterStateCapabilities(
                    state,
                    CharacterSuperstate.Grounded,
                    canMove: true,
                    canToggleWeapon: false),

                CharacterState.LightFlinch => new CharacterStateCapabilities(
                    state,
                    CharacterSuperstate.HitReaction,
                    canMove: true,
                    canToggleWeapon: false),

                CharacterState.HeavyStagger => new CharacterStateCapabilities(
                    state,
                    CharacterSuperstate.HitReaction,
                    canMove: true,
                    canToggleWeapon: false),

                CharacterState.Knockdown => new CharacterStateCapabilities(
                    state,
                    CharacterSuperstate.Incapacitated,
                    canMove: false,
                    canToggleWeapon: false,
                    canReceiveHit: false),

                CharacterState.ForcedKnockdown => new CharacterStateCapabilities(
                    state,
                    CharacterSuperstate.Incapacitated,
                    canMove: false,
                    canToggleWeapon: false,
                    canReceiveHit: false),

                CharacterState.Recovering => new CharacterStateCapabilities(
                    state,
                    CharacterSuperstate.Incapacitated,
                    canMove: false,
                    canToggleWeapon: false,
                    canReceiveHit: false),

                _ => new CharacterStateCapabilities(
                    state,
                    CharacterSuperstate.Grounded,
                    canMove: false,
                    canToggleWeapon: false)
            };
        }
    }
}
