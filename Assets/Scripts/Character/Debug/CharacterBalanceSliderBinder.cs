using UnityEngine;
using UnityEngine.UI;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 将角色平衡值同步到 UI Slider
    /// Sync character balance value to a UI Slider
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterBalanceSliderBinder : MonoBehaviour
    {
        [SerializeField] CharacterControllerRoot target;
        [SerializeField] Slider slider;
        [SerializeField] bool normalizeValue = true;

        void Reset()
        {
            slider = GetComponent<Slider>();
        }

        void Awake()
        {
            if (slider == null)
                slider = GetComponent<Slider>();
        }

        void LateUpdate()
        {
            if (target == null || slider == null)
                return;

            var context = target.Context;
            var maxBalance = Mathf.Max(1, context.MaxBalance);
            var currentBalance = Mathf.Clamp(context.CurrentBalance, 0, maxBalance);

            if (normalizeValue)
            {
                slider.minValue = 0f;
                slider.maxValue = 1f;
                slider.value = currentBalance / (float)maxBalance;
            }
            else
            {
                slider.minValue = 0f;
                slider.maxValue = maxBalance;
                slider.value = currentBalance;
            }
        }
    }
}
