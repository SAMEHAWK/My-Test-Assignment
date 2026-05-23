using UnityEngine;
using UnityEngine.InputSystem;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 读取 Input System 的 Move、Equip 并转发给 CharacterControllerRoot
    /// Reads Input System Move and Equip actions and forwards to CharacterControllerRoot
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [SerializeField] InputActionAsset inputActions;
        [SerializeField] string actionMapName = "Player";
        [SerializeField] string moveActionName = "Move";
        [SerializeField] string equipActionName = "Equip";

        InputAction _moveAction;
        InputAction _equipAction;
        CharacterControllerRoot _controllerRoot;

        void Awake()
        {
            _controllerRoot = GetComponent<CharacterControllerRoot>();

            if (_controllerRoot == null)
            {
                Debug.LogError(
                    "[PlayerInputReader] 同物体上需要 Character Controller Root 组件\n" +
                    "[PlayerInputReader] CharacterControllerRoot required on the same GameObject.",
                    this);
                return;
            }

            if (inputActions == null)
            {
                Debug.LogWarning(
                    "[PlayerInputReader] 未分配 InputActionAsset，请拖入 Assets/InputSystem_Actions.inputactions\n" +
                    "[PlayerInputReader] InputActionAsset not assigned.",
                    this);
                return;
            }

            var map = inputActions.FindActionMap(actionMapName, true);
            _moveAction = map.FindAction(moveActionName, true);
            _equipAction = map.FindAction(equipActionName, true);
        }

        void OnEnable()
        {
            _moveAction?.Enable();
            _equipAction?.Enable();
        }

        void OnDisable()
        {
            _moveAction?.Disable();
            _equipAction?.Disable();
        }

        void Update()
        {
            if (_controllerRoot == null)
                return;

            if (_moveAction != null)
                _controllerRoot.SetMoveInput(_moveAction.ReadValue<Vector2>());

            if (_equipAction != null && _equipAction.WasPressedThisFrame())
                _controllerRoot.TryToggleWeaponEquip();
        }
    }
}
