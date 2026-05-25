using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 复制 Player 自动攻击驱动：禁用输入，自动拔刀并按间隔攻击
    /// Clone player auto-attack driver: disables input, equips weapon, then attacks on interval
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ClonePlayerAttackDriver : MonoBehaviour
    {
        [SerializeField] CharacterControllerRoot root;
        [SerializeField] bool disableInputReaderOnAwake = true;
        [SerializeField] CharacterAttackType attackType = CharacterAttackType.Heavy;
        [SerializeField] float attackInterval = 2f;
        [SerializeField] bool attackImmediatelyAfterEquipped = true;

        float _attackTimer;

        void Awake()
        {
            if (root == null)
                root = GetComponent<CharacterControllerRoot>();

            if (disableInputReaderOnAwake && TryGetComponent<PlayerInputReader>(out var inputReader))
                inputReader.enabled = false;
        }

        void OnEnable()
        {
            _attackTimer = attackImmediatelyAfterEquipped ? Mathf.Max(0.01f, attackInterval) : 0f;
        }

        void OnValidate()
        {
            attackInterval = Mathf.Max(0.01f, attackInterval);
        }

        void Update()
        {
            if (root == null)
                return;
            if (root.IsInKnockdownPhase || root.IsInRecoveryPhase)
                return;

            if (!root.IsWeaponEquipped)
            {
                TryEquipWeapon();
                return;
            }

            if (root.CurrentState != CharacterState.Locomotion)
                return;

            _attackTimer += Time.deltaTime;
            if (_attackTimer < Mathf.Max(0.01f, attackInterval))
                return;

            TryAttack();
        }

        void TryEquipWeapon()
        {
            if (!root.CanToggleWeapon || root.CurrentState != CharacterState.Locomotion)
                return;

            if (root.TryToggleWeaponEquip())
                _attackTimer = attackImmediatelyAfterEquipped ? Mathf.Max(0.01f, attackInterval) : 0f;
        }

        void TryAttack()
        {
            var started = attackType == CharacterAttackType.Heavy
                ? root.TryHeavyAttack()
                : root.TryLightAttack();

            // 无论本次是否成功，都按间隔节流，避免动画/配置缺失时每帧重试
            // Throttle attempts even on failure to avoid per-frame retries when animation/config is missing
            _attackTimer = 0f;
            _ = started;
        }
    }
}
