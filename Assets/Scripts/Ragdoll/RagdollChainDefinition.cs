using System;
using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 受击链传播模式：只自身 / 自身+子级 / 自身+父子级
    /// Chain propagation mode: self only / self + children / self + parent and children
    /// </summary>
    public enum RagdollChainPropagationMode
    {
        SelfOnly,
        Children,
        ParentAndChildren
    }

    /// <summary>
    /// Ragdoll 局部受击链定义：用关键字匹配骨骼，并提供链级调参
    /// Ragdoll partial-reaction chain definition: keyword-based bone matching with chain-level tuning
    /// </summary>
    [Serializable]
    public sealed class RagdollChainDefinition
    {
        [Tooltip("链名称（用于调试显示）— Chain name for debug display")]
        public string chainName = "Unnamed";

        [Tooltip("骨骼名关键字列表（大小写不敏感）— Bone-name keywords (case-insensitive)")]
        public string[] boneNameKeywords = Array.Empty<string>();

        [Tooltip("传播模式：SelfOnly / Children / ParentAndChildren — Propagation mode")]
        public RagdollChainPropagationMode propagationMode = RagdollChainPropagationMode.ParentAndChildren;

        [Tooltip("是否允许补充 primary 骨骼的子节点 — Allow adding descendants of primary bone")]
        public bool includeChildren = true;

        [Tooltip("向上父链最大层级（ParentAndChildren 模式）— Max parent depth for ParentAndChildren mode")]
        [Min(0)]
        public int maxParentDepth = 2;

        [Tooltip("向下子链最大层级（Children / ParentAndChildren）— Max child depth for child propagation")]
        [Min(0)]
        public int maxChildDepth = 2;

        [Tooltip("每层父链衰减（0~1）— Parent falloff per hierarchy depth")]
        [Range(0f, 1f)]
        public float parentFalloff = 0.6f;

        [Tooltip("每层子链衰减（0~1）— Child falloff per hierarchy depth")]
        [Range(0f, 1f)]
        public float childFalloff = 0.75f;

        [Tooltip("传播权重最小阈值（小于此值会被丢弃）— Minimum propagation weight threshold")]
        [Range(0f, 1f)]
        public float minPropagationWeight = 0.2f;

        [Tooltip("链级冲量倍率 — Chain-level impulse multiplier")]
        public float impulseMultiplier = 1f;

        [Tooltip("链级回写权重（0~1）— Chain-level writeback weight")]
        [Range(0f, 1f)]
        public float writebackWeight = 1f;

        public bool MatchesBoneName(string boneName)
        {
            if (string.IsNullOrWhiteSpace(boneName) || boneNameKeywords == null || boneNameKeywords.Length == 0)
                return false;

            for (var i = 0; i < boneNameKeywords.Length; i++)
            {
                var keyword = boneNameKeywords[i];
                if (string.IsNullOrWhiteSpace(keyword))
                    continue;
                if (boneName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }
    }
}
