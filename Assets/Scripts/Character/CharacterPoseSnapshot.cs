using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 角色姿态快照：用于在表现系统之间传递骨骼局部旋转
    /// Character pose snapshot — transfers bone local rotations between presentation systems
    /// </summary>
    public readonly struct CharacterPoseSnapshot
    {
        public readonly Transform[] Bones;
        public readonly Quaternion[] LocalRotations;

        public bool IsValid =>
            Bones != null &&
            LocalRotations != null &&
            Bones.Length > 0 &&
            Bones.Length == LocalRotations.Length;

        public CharacterPoseSnapshot(Transform[] bones, Quaternion[] localRotations)
        {
            Bones = bones;
            LocalRotations = localRotations;
        }
    }
}
