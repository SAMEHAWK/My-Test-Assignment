using System;
using System.Collections.Generic;
using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 双骨架骨骼映射调试组件：按同名匹配 Visual/Physics 骨骼并输出缺失项
    /// Dual-skeleton mapping debug component: name-based Visual/Physics matching with missing reports
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RagdollBoneMapper : MonoBehaviour
    {
        [Serializable]
        public struct BonePair
        {
            public string BoneName;
            public Transform VisualBone;
            public Transform PhysicsBone;
            public Rigidbody PhysicsBody;
        }

        [Header("References / 引用")]
        [SerializeField] Transform visualRoot;
        [SerializeField] Transform physicsRoot;
        [SerializeField] bool includeInactive = true;
        [SerializeField] bool logResultOnRebuild = true;

        [Header("Runtime Snapshot / 运行时快照")]
        [SerializeField] int mappedBoneCount;
        [SerializeField] string[] missingInVisual = Array.Empty<string>();
        [SerializeField] string[] missingInPhysics = Array.Empty<string>();
        [SerializeField] BonePair[] mappedPairs = Array.Empty<BonePair>();

        public int MappedBoneCount => mappedBoneCount;
        public IReadOnlyList<string> MissingInVisual => missingInVisual;
        public IReadOnlyList<string> MissingInPhysics => missingInPhysics;
        public IReadOnlyList<BonePair> MappedPairs => mappedPairs;

        [ContextMenu("Rebuild Bone Map / 重建骨骼映射")]
        public void RebuildMap()
        {
            BuildMapInternal(logResultOnRebuild);
        }

        void Awake()
        {
            if (mappedPairs == null || mappedPairs.Length == 0)
                BuildMapInternal(logResultOnRebuild);
        }

        void OnValidate()
        {
            if (!Application.isPlaying)
                BuildMapInternal(logResult: false);
        }

        bool BuildMapInternal(bool logResult)
        {
            mappedBoneCount = 0;
            missingInVisual = Array.Empty<string>();
            missingInPhysics = Array.Empty<string>();
            mappedPairs = Array.Empty<BonePair>();

            if (visualRoot == null || physicsRoot == null)
            {
                if (logResult)
                {
                    Debug.LogWarning(
                        "[RagdollBoneMapper] Visual Root 或 Physics Root 未设置，无法建立映射。\n" +
                        "[RagdollBoneMapper] Visual Root or Physics Root is not assigned; mapping skipped.",
                        this);
                }
                return false;
            }

            var visualByName = CollectBonesByName(visualRoot);
            var physicsByName = CollectBonesByName(physicsRoot);
            var physicsBodiesByName = CollectPhysicsBodiesByName(physicsRoot);

            var mappedList = new List<BonePair>(physicsBodiesByName.Count);
            var missingVisualList = new List<string>();
            var missingPhysicsList = new List<string>();

            foreach (var visualName in visualByName.Keys)
            {
                if (!physicsBodiesByName.ContainsKey(visualName))
                    missingInPhysics = Append(missingInPhysics, visualName);
            }

            foreach (var kv in physicsBodiesByName)
            {
                var boneName = kv.Key;
                var physicsBody = kv.Value;
                if (!visualByName.TryGetValue(boneName, out var visualBone))
                {
                    missingVisualList.Add(boneName);
                    continue;
                }

                physicsByName.TryGetValue(boneName, out var physicsBone);
                mappedList.Add(new BonePair
                {
                    BoneName = boneName,
                    VisualBone = visualBone,
                    PhysicsBone = physicsBone,
                    PhysicsBody = physicsBody
                });
            }

            missingInVisual = missingVisualList.ToArray();
            missingInPhysics = DeduplicateAndSort(missingInPhysics);
            mappedPairs = mappedList.ToArray();
            mappedBoneCount = mappedPairs.Length;

            if (logResult)
                LogBuildSummary();

            return mappedBoneCount > 0;
        }

        Dictionary<string, Transform> CollectBonesByName(Transform root)
        {
            var result = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            if (root == null)
                return result;

            foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive))
            {
                if (t == null)
                    continue;
                if (!result.ContainsKey(t.name))
                    result.Add(t.name, t);
            }

            return result;
        }

        Dictionary<string, Rigidbody> CollectPhysicsBodiesByName(Transform root)
        {
            var result = new Dictionary<string, Rigidbody>(StringComparer.OrdinalIgnoreCase);
            if (root == null)
                return result;

            foreach (var rb in root.GetComponentsInChildren<Rigidbody>(includeInactive))
            {
                if (rb == null)
                    continue;
                if (!result.ContainsKey(rb.transform.name))
                    result.Add(rb.transform.name, rb);
            }

            return result;
        }

        static string[] Append(string[] source, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return source;

            var arr = source ?? Array.Empty<string>();
            Array.Resize(ref arr, arr.Length + 1);
            arr[^1] = value;
            return arr;
        }

        static string[] DeduplicateAndSort(string[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<string>();

            var set = new HashSet<string>(source, StringComparer.OrdinalIgnoreCase);
            var list = new List<string>(set);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list.ToArray();
        }

        void LogBuildSummary()
        {
            var missingVisualText = missingInVisual.Length > 0
                ? string.Join(", ", missingInVisual)
                : "None";
            var missingPhysicsText = missingInPhysics.Length > 0
                ? string.Join(", ", missingInPhysics)
                : "None";

            Debug.Log(
                $"[RagdollBoneMapper] MappedBoneCount={mappedBoneCount}\n" +
                $"[RagdollBoneMapper] MissingInVisual={missingVisualText}\n" +
                $"[RagdollBoneMapper] MissingInPhysics={missingPhysicsText}",
                this);
        }
    }
}
