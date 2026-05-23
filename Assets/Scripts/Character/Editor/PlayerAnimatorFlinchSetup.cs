#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ActiveRagdoll.Character.Editor
{
    /// <summary>
    /// C2：FlinchLayer — Additive + 2D 方向 Blend + UpBody Mask
    /// C2 — FlinchLayer additive 2D directional blend with upper-body mask
    /// </summary>
    public static class PlayerAnimatorFlinchSetup
    {
        const string ControllerPath = "Assets/Animator/Player_Animator Controller.controller";
        const string MaskPath = "Assets/Models/UpBody.mask";
        const string FlinchLayerName = "FlinchLayer";
        const string HitBlendX = "HitBlendX";
        const string HitBlendZ = "HitBlendZ";

        [MenuItem("Active Ragdoll/Setup Player Flinch Layer (C2)")]
        public static void SetupFlinchLayer()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                Debug.LogError(
                    $"[C2] 未找到 Animator Controller: {ControllerPath}\n" +
                    $"[C2] Animator Controller not found: {ControllerPath}");
                return;
            }

            RemoveParameterIfExists(controller, "HitDirection");

            EnsureParameter(controller, HitBlendX, AnimatorControllerParameterType.Float);
            EnsureParameter(controller, HitBlendZ, AnimatorControllerParameterType.Float);
            EnsureParameter(controller, "FlinchWeight", AnimatorControllerParameterType.Float);

            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
            if (mask == null)
            {
                Debug.LogWarning(
                    $"[C2] 未找到 UpBody Mask: {MaskPath}\n" +
                    $"[C2] Avatar mask not found: {MaskPath}");
            }

            var layerIndex = FindLayerIndex(controller, FlinchLayerName);
            if (layerIndex < 0)
            {
                controller.AddLayer(FlinchLayerName);
                layerIndex = controller.layers.Length - 1;
                const int targetIndex = 1;
                if (layerIndex != targetIndex)
                    layerIndex = ReorderLayerToIndex(controller, layerIndex, targetIndex);
            }

            var layers = controller.layers;
            layers[layerIndex].avatarMask = mask;
            layers[layerIndex].blendingMode = AnimatorLayerBlendingMode.Additive;
            layers[layerIndex].defaultWeight = 1f;
            controller.layers = layers;

            var front = LoadClip("Assets/Models/ExtractedClips/hit_front_inplace.anim");
            var back = LoadClip("Assets/Models/ExtractedClips/hit_back_inplace.anim");
            var left = LoadClip("Assets/Models/ExtractedClips/hit_left_inplace.anim");
            var right = LoadClip("Assets/Models/ExtractedClips/hit_right_inplace.anim");

            var stateMachine = layers[layerIndex].stateMachine;
            foreach (var child in stateMachine.states)
                stateMachine.RemoveState(child.state);

            var blendTree = new BlendTree
            {
                name = "FlinchDirectional2D",
                blendType = BlendTreeType.FreeformDirectional2D,
                blendParameter = HitBlendX,
                blendParameterY = HitBlendZ,
                useAutomaticThresholds = false
            };

            if (front != null)
                blendTree.AddChild(front, new Vector2(0f, 1f));
            if (back != null)
                blendTree.AddChild(back, new Vector2(0f, -1f));
            if (left != null)
                blendTree.AddChild(left, new Vector2(-1f, 0f));
            if (right != null)
                blendTree.AddChild(right, new Vector2(1f, 0f));

            AssetDatabase.AddObjectToAsset(blendTree, controller);

            var state = stateMachine.AddState("Flinch", new Vector3(280f, 0f, 0f));
            state.motion = blendTree;
            stateMachine.defaultState = state;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "[C2] FlinchLayer: Additive, HitBlendX/Z 2D Directional, UpBody.mask\n" +
                "[C2] FlinchLayer setup complete.",
                controller);
        }

        static void RemoveParameterIfExists(AnimatorController controller, string name)
        {
            for (var i = 0; i < controller.parameters.Length; i++)
            {
                if (controller.parameters[i].name != name)
                    continue;
                controller.RemoveParameter(i);
                return;
            }
        }

        /// <summary>
        /// Unity 6 无 MoveLayer API，通过重写 layers 数组调整顺序
        /// Reorder layer by rewriting layers array (no MoveLayer in Unity 6)
        /// </summary>
        static int ReorderLayerToIndex(AnimatorController controller, int fromIndex, int toIndex)
        {
            var layers = new List<AnimatorControllerLayer>(controller.layers);
            if (fromIndex < 0 || fromIndex >= layers.Count || toIndex < 0 || toIndex >= layers.Count)
                return fromIndex;

            var layer = layers[fromIndex];
            layers.RemoveAt(fromIndex);
            layers.Insert(toIndex, layer);
            controller.layers = layers.ToArray();
            return toIndex;
        }

        static int FindLayerIndex(AnimatorController controller, string layerName)
        {
            for (var i = 0; i < controller.layers.Length; i++)
            {
                if (controller.layers[i].name == layerName)
                    return i;
            }

            return -1;
        }

        static void EnsureParameter(
            AnimatorController controller,
            string name,
            AnimatorControllerParameterType type)
        {
            foreach (var p in controller.parameters)
            {
                if (p.name == name)
                    return;
            }

            controller.AddParameter(name, type);
        }

        static AnimationClip LoadClip(string assetPath)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (clip == null)
            {
                Debug.LogWarning(
                    $"[C2] 缺少动画片段: {assetPath}\n" +
                    $"[C2] Missing animation clip: {assetPath}");
            }

            return clip;
        }
    }
}
#endif
