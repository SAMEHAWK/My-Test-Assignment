#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ActiveRagdoll.Character.Editor
{
    /// <summary>
    /// Character Controller Root 检视器扩展 — 一键收集 Ragdoll 刚体
    /// Inspector extension — one-click ragdoll rigidbody collection
    /// </summary>
    [CustomEditor(typeof(CharacterControllerRoot))]
    public sealed class CharacterControllerRootEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(8f);

            var root = (CharacterControllerRoot)target;

            if (GUILayout.Button("收集 Ragdoll Rigidbody（从 Model 子层级）\nCollect Ragdoll Rigidbodies From Children", GUILayout.Height(36f)))
            {
                root.CollectRagdollBodiesFromChildren();
                EditorUtility.SetDirty(root);
            }

            EditorGUILayout.HelpBox(
                "从 Ragdoll Search Root（未指定则用 Animator 物体）向下收集所有 Rigidbody，按名称排序写入数组。可关闭 Auto Collect Ragdoll If Empty 改为仅手动收集。\n" +
                "Collects Rigidbodies under search root (Animator if unset). Disable auto-collect to fill manually only.",
                MessageType.Info);
        }
    }
}
#endif
