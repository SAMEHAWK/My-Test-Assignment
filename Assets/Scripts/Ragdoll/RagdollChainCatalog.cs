using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// Ragdoll 受击链目录：按命中骨骼名解析所属链
    /// Ragdoll chain catalog: resolves chain by contacted bone name
    /// </summary>
    [CreateAssetMenu(fileName = "RagdollChainCatalog", menuName = "Active Ragdoll/Ragdoll Chain Catalog")]
    public sealed class RagdollChainCatalog : ScriptableObject
    {
        [Tooltip("受击链定义列表 — Partial-reaction chain definitions")]
        public RagdollChainDefinition[] chains;

        void OnEnable()
        {
            EnsureDefaultPresetIfEmpty();
        }

        [ContextMenu("Apply Default Preset / 应用默认链参数")]
        public void ApplyDefaultPreset()
        {
            chains = BuildDefaultChains();
        }

        /// <summary>
        /// 仅当链列表为空时自动填充默认参数，避免覆盖手工调参
        /// Auto-fill defaults only when list is empty; never overwrite manual tuning
        /// </summary>
        public void EnsureDefaultPresetIfEmpty()
        {
            if (chains != null && chains.Length > 0)
                return;

            chains = BuildDefaultChains();
        }

        public bool TryResolveByBoneName(string boneName, out RagdollChainDefinition chain)
        {
            chain = null;
            if (string.IsNullOrWhiteSpace(boneName) || chains == null || chains.Length == 0)
                return false;

            for (var i = 0; i < chains.Length; i++)
            {
                var candidate = chains[i];
                if (candidate == null)
                    continue;
                if (!candidate.MatchesBoneName(boneName))
                    continue;
                chain = candidate;
                return true;
            }

            return false;
        }

        static RagdollChainDefinition[] BuildDefaultChains()
        {
            return new[]
            {
                CreateChain(
                    "head",
                    new[] { "head" },
                    includeChildren: false,
                    maxParentDepth: 1,
                    maxChildDepth: 0),
                CreateChain(
                    "chest",
                    new[] { "chest", "upperchest", "spine" },
                    includeChildren: true,
                    maxParentDepth: 1,
                    maxChildDepth: 2),
                CreateChain(
                    "hips",
                    new[] { "hips", "pelvis" },
                    includeChildren: true,
                    maxParentDepth: 0,
                    maxChildDepth: 2),
                CreateChain(
                    "leftarm",
                    new[] { "leftarm", "leftupperarm", "leftforearm", "lefthand" },
                    includeChildren: true,
                    maxParentDepth: 2,
                    maxChildDepth: 2),
                CreateChain(
                    "rightarm",
                    new[] { "rightarm", "rightupperarm", "rightforearm", "righthand" },
                    includeChildren: true,
                    maxParentDepth: 2,
                    maxChildDepth: 2),
                CreateChain(
                    "leftleg",
                    new[] { "leftupleg", "leftleg", "leftfoot" },
                    includeChildren: true,
                    maxParentDepth: 2,
                    maxChildDepth: 2),
                CreateChain(
                    "rightleg",
                    new[] { "rightupleg", "rightleg", "rightfoot" },
                    includeChildren: true,
                    maxParentDepth: 2,
                    maxChildDepth: 2)
            };
        }

        static RagdollChainDefinition CreateChain(
            string name,
            string[] keywords,
            bool includeChildren,
            int maxParentDepth,
            int maxChildDepth)
        {
            return new RagdollChainDefinition
            {
                chainName = name,
                boneNameKeywords = keywords,
                propagationMode = RagdollChainPropagationMode.ParentAndChildren,
                includeChildren = includeChildren,
                maxParentDepth = maxParentDepth,
                maxChildDepth = maxChildDepth,
                parentFalloff = 0.6f,
                childFalloff = 0.75f,
                minPropagationWeight = 0.2f,
                impulseMultiplier = 1f,
                writebackWeight = 1f
            };
        }
    }
}
