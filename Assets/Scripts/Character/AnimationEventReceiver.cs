using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 挂在 Model（与 Animator 同物体），集中接收所有 Animation Clip 事件
    /// On Model with Animator — single entry for all animation clip events
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AnimationEventReceiver : MonoBehaviour
    {
        [Header("Weapon / 武器")]
        [Tooltip("装备后显示在手上 — Shown on hand when equipped")]
        [SerializeField] GameObject handWeaponMesh;
        [Tooltip("未装备时显示在背上 — Shown on back when unequipped")]
        [SerializeField] GameObject backWeaponMesh;

        CharacterControllerRoot _root;
        Animator _animator;
        Vector3 _initialLocalPosition;
        bool _hasInitialLocalPosition;

        void Awake()
        {
            _root = GetComponentInParent<CharacterControllerRoot>();
            _animator = GetComponent<Animator>();
            _initialLocalPosition = transform.localPosition;
            _hasInitialLocalPosition = true;
            if (_root == null)
            {
                Debug.LogError(
                    "[AnimationEventReceiver] 未找到父级 CharacterControllerRoot\n" +
                    "[AnimationEventReceiver] CharacterControllerRoot not found in parents.",
                    this);
            }
        }

        void OnAnimatorMove()
        {
            if (_root == null || _animator == null)
                return;

            _root.ConsumeGameplayRootMotion(_animator.deltaPosition);
        }

        void LateUpdate()
        {
            if (_root == null || !_root.AllowsGameplayRootMotion || !_hasInitialLocalPosition)
                return;

            // 避免 Animator 子节点在启用根运动后逐帧累计本地偏移，导致模型中心漂移
            // Prevent per-frame local-position accumulation on animator child when root motion is enabled
            transform.localPosition = _initialLocalPosition;
        }

        // === Clip 中 Function 名须与下列 public 方法一致 ===
        // === Function names in clips must match these public methods ===

        /// <summary>
        /// 装备动画抓到武器帧：Mesh + 已装备状态（Equipped），不归零 Layer
        /// Grab frame — mesh and equipped state; overlay layers keep playing
        /// </summary>
        public void OnWeaponEquipShow()
        {
            SetWeaponMeshesVisible(equipped: true);
            _root?.NotifyWeaponEquipped();
        }

        /// <summary>
        /// 装备动画结束帧：仅归零 FullBody/UpBody
        /// End of equip clip — reset overlay layers only
        /// </summary>
        public void OnWeaponEquipFinished()
        {
            _root?.NotifyWeaponEquipPlaybackFinished();
        }

        /// <summary>
        /// 收回动画武器回背上帧：Mesh + 未装备状态
        /// Weapon stowed on back — mesh and unequipped state
        /// </summary>
        public void OnWeaponUnequipHide()
        {
            SetWeaponMeshesVisible(equipped: false);
            _root?.NotifyWeaponUnequipped();
        }

        /// <summary>
        /// 收回动画结束帧：仅归零 overlay 层
        /// End of unequip clip — reset overlay layers only
        /// </summary>
        public void OnWeaponUnequipFinished()
        {
            _root?.NotifyWeaponUnequipPlaybackFinished();
        }

        /// <summary>
        /// 起身动画结束帧：通知 Root 可退出 Recovering
        /// Get-up clip end frame — notify Root recovery can complete
        /// </summary>
        public void OnGetUpFinished()
        {
            _root?.NotifyGetUpFinished();
        }

        /// <summary>
        /// 重击动画结束帧：通知 Root 可退出 HeavyStagger 并恢复输入
        /// Heavy-stagger end frame: notify Root to leave HeavyStagger and restore input
        /// </summary>
        public void OnHeavyStaggerFinished()
        {
            _root?.NotifyHeavyStaggerFinished();
        }

        /// <summary>
        /// 主动攻击动画结束帧：释放攻击冻结
        /// Player attack clip end — release attack lock
        /// </summary>
        public void OnAttackFinished()
        {
            _root?.NotifyAttackFinished();
        }

        /// <summary>
        /// 轻攻击有效命中窗口开始
        /// Light attack active hit window start
        /// </summary>
        public void OnLightAttackHitStart()
        {
            _root?.NotifyLightAttackHitStart();
        }

        /// <summary>
        /// 重攻击有效命中窗口开始
        /// Heavy attack active hit window start
        /// </summary>
        public void OnHeavyAttackHitStart()
        {
            _root?.NotifyHeavyAttackHitStart();
        }

        /// <summary>
        /// 攻击有效命中窗口结束
        /// Attack active hit window end
        /// </summary>
        public void OnAttackHitEnd()
        {
            _root?.NotifyAttackHitEnd();
        }

        /// <summary>
        /// 仅切换武器网格显隐（层权重与 Equipped 由 CharacterAnimationPresenter 在按键/Notify 时处理）
        /// Toggle weapon mesh visibility only
        /// </summary>
        void SetWeaponMeshesVisible(bool equipped)
        {
            if (handWeaponMesh != null)
                handWeaponMesh.SetActive(equipped);

            if (backWeaponMesh != null)
                backWeaponMesh.SetActive(!equipped);
        }
    }
}
