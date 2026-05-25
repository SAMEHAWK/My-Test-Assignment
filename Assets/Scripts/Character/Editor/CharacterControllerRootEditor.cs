#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ActiveRagdoll.Character.Editor
{
    /// <summary>
    /// Character Controller Root 检视器扩展 — 双骨架配置提示
    /// Inspector extension — dual-skeleton setup reminder
    /// </summary>
    [CustomEditor(typeof(CharacterControllerRoot))]
    public sealed class CharacterControllerRootEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(8f);

            EditorGUILayout.HelpBox(
                "当前版本仅支持双骨架后端，请确保 CharacterRagdollSystem 的 Visual Root / Physics Root / Animator 已正确引用。\n" +
                "Current version supports dual-skeleton backend only. Ensure Visual Root / Physics Root / Animator references are valid.",
                MessageType.Info);
        }
    }
}
#endif
