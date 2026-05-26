using System;
using UnityEngine;

namespace ActiveRagdoll.Character
{
    /// <summary>
    /// 起身对齐：击倒后恢复时根节点位置/朝向计算与落地检测
    /// Recovery alignment: root position/orientation and ground detection on get-up
    /// </summary>
    public sealed class CharacterRecoveryAlignment
    {
        readonly Transform _rootTransform;
        readonly Animator _animator;
        readonly UnityEngine.CharacterController _cc;
        readonly CharacterAnimationConfig _animConfig;
        readonly bool _preAlignRotation;
        readonly float _forceAlignMinAngle;

        Vector3 _initialRootMinusHipsOffset;
        bool _hasInitialRootMinusHipsOffset;

        public CharacterRecoveryAlignment(
            Transform rootTransform,
            Animator animator,
            UnityEngine.CharacterController cc,
            CharacterAnimationConfig animConfig,
            bool preAlignRotation,
            float forceAlignMinAngle)
        {
            _rootTransform = rootTransform;
            _animator = animator;
            _cc = cc;
            _animConfig = animConfig;
            _preAlignRotation = preAlignRotation;
            _forceAlignMinAngle = forceAlignMinAngle;
            CacheInitialOffset();
        }

        void CacheInitialOffset()
        {
            if (!TryGetHumanoidHips(out var hips))
            {
                _hasInitialRootMinusHipsOffset = false;
                return;
            }

            _initialRootMinusHipsOffset = _rootTransform.position - hips.position;
            _hasInitialRootMinusHipsOffset = true;
        }

        bool TryGetHumanoidHips(out Transform hips)
        {
            hips = null;
            if (_animator == null || !_animator.isHuman || _animator.avatar == null)
                return false;

            hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            return hips != null;
        }

        /// <summary>
        /// 执行完整的起身前对齐：位置校正 + 朝向判定与旋转
        /// Full pre-recovery alignment: position correction + facing resolve and rotation
        /// </summary>
        public void AlignForRecovery(
            ref RagdollAnchor recoveryAnchor,
            RecoveryGetUpType getUpType,
            out Vector3 resolvedForward,
            out bool shouldFlipBack,
            out float angleToAnchor,
            out bool appliedVisualRotation)
        {
            var currentForward = ResolveCurrentVisualForward();

            if (!recoveryAnchor.IsValid)
            {
#if UNITY_EDITOR
                Debug.LogWarning(
                    "[CharacterRecoveryAlignment] Recovery anchor 无效，使用当前根位置与朝向回退。\n" +
                    "[CharacterRecoveryAlignment] Recovery anchor invalid; fallback to current root position/forward.");
#endif
                recoveryAnchor = new RagdollAnchor(_rootTransform.position, currentForward, true);
            }

            AlignRootPosition(in recoveryAnchor);
            resolvedForward = ResolveRecoveryForward(currentForward, recoveryAnchor.FacingForward, getUpType, out shouldFlipBack, out angleToAnchor);
            appliedVisualRotation = ShouldApplyRecoveryVisualAlign(angleToAnchor);
            if (appliedVisualRotation)
                ApplyRecoveryVisualRotation(resolvedForward);

#if UNITY_EDITOR
            if (recoveryAnchor.IsValid)
            {
                var currentYaw = Mathf.Atan2(currentForward.x, currentForward.z) * Mathf.Rad2Deg;
                var anchorYaw = Mathf.Atan2(recoveryAnchor.FacingForward.x, recoveryAnchor.FacingForward.z) * Mathf.Rad2Deg;
                Debug.Log(
                    $"[RecoveryAlign] preAlignRotation={_preAlignRotation}, effectiveAlign={appliedVisualRotation}, getUpType={getUpType}, " +
                    $"currentYaw={currentYaw:F1}, anchorYaw={anchorYaw:F1}, angle={angleToAnchor:F1}, autoFlipBack={shouldFlipBack}");
            }
#endif
        }

        void AlignRootPosition(in RagdollAnchor anchor)
        {
            if (!anchor.IsValid)
                return;

            var targetRootPosition = anchor.HipsWorldPosition;
            if (_hasInitialRootMinusHipsOffset)
                targetRootPosition += _initialRootMinusHipsOffset;
            else
                targetRootPosition.y = _rootTransform.position.y;

            // 防止直接使用 ragdoll hips 的低位 Y 导致起身开场沉地
            // Avoid sinking: align by XZ and solve a grounded Y for CharacterController
            targetRootPosition.y = ResolveRecoveryRootY(targetRootPosition, anchor.HipsWorldPosition.y);

            var ccWasEnabled = _cc != null && _cc.enabled;
            if (ccWasEnabled)
                _cc.enabled = false;

            _rootTransform.position = targetRootPosition;

            if (ccWasEnabled)
                _cc.enabled = true;
        }

        Vector3 ResolveCurrentVisualForward()
        {
            var forward = _animator != null ? _animator.transform.forward : _rootTransform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = _rootTransform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        Vector3 ResolveRecoveryForward(
            Vector3 currentForward,
            Vector3 anchorForward,
            RecoveryGetUpType getUpType,
            out bool shouldFlipBack,
            out float angleToAnchor)
        {
            var normalizedCurrent = currentForward;
            normalizedCurrent.y = 0f;
            if (normalizedCurrent.sqrMagnitude < 0.0001f)
                normalizedCurrent = _rootTransform.forward;
            normalizedCurrent.y = 0f;
            normalizedCurrent = normalizedCurrent.sqrMagnitude > 0.0001f ? normalizedCurrent.normalized : Vector3.forward;

            var normalizedAnchor = anchorForward;
            normalizedAnchor.y = 0f;
            if (normalizedAnchor.sqrMagnitude < 0.0001f)
                normalizedAnchor = normalizedCurrent;
            normalizedAnchor = normalizedAnchor.normalized;

            angleToAnchor = Vector3.Angle(normalizedCurrent, normalizedAnchor);
            // 固定主链路：Front=hips->head，Back=head->hips（即 anchor 反向）
            // Fixed path: Front=hips->head, Back=head->hips (invert anchor for Back)
            shouldFlipBack = getUpType == RecoveryGetUpType.Back;

            return shouldFlipBack ? -normalizedAnchor : normalizedAnchor;
        }

        bool ShouldApplyRecoveryVisualAlign(float angleToAnchor)
        {
            if (_preAlignRotation)
                return true;

            var threshold = Mathf.Clamp(_forceAlignMinAngle, 0f, 180f);
            return angleToAnchor >= threshold;
        }

        void ApplyRecoveryVisualRotation(Vector3 resolvedForward)
        {
            if (_animator == null)
                return;

            var planarForward = resolvedForward;
            planarForward.y = 0f;
            if (planarForward.sqrMagnitude < 0.0001f)
                return;

            _animator.transform.rotation = Quaternion.LookRotation(planarForward.normalized, Vector3.up);
        }

        float ResolveRecoveryRootY(Vector3 targetRootPosition, float hipsWorldY)
        {
            var fallbackY = _rootTransform.position.y;
            if (_cc == null)
                return fallbackY;

            var rayOrigin = new Vector3(
                targetRootPosition.x,
                Mathf.Max(hipsWorldY + 1.5f, fallbackY + 1.5f),
                targetRootPosition.z);
            if (!TryFindGroundHit(rayOrigin, 6f, out var hit))
                return fallbackY;

            var halfHeight = _cc.height * 0.5f;
            var solvedY = hit.point.y - _cc.center.y + halfHeight + _cc.skinWidth;
            return solvedY;
        }

        bool TryFindGroundHit(Vector3 origin, float maxDistance, out RaycastHit groundHit)
        {
            var hits = Physics.RaycastAll(
                origin,
                Vector3.down,
                maxDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            if (hits == null || hits.Length == 0)
            {
                groundHit = default;
                return false;
            }

            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (var i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                var hitTransform = hit.collider != null ? hit.collider.transform : null;
                if (hitTransform != null && hitTransform.IsChildOf(_rootTransform))
                    continue;

                groundHit = hit;
                return true;
            }

            groundHit = default;
            return false;
        }

        /// <summary>
        /// 兜底解锁时长覆盖：PoseMatch + CrossFade + 目标起身时长 + 0.8 秒缓冲
        /// Fallback duration covering PoseMatch + CrossFade + get-up clip + buffer
        /// </summary>
        public float ResolveRecoveryFallbackDuration(RecoveryGetUpType getUpType)
        {
            if (_animConfig == null)
                return 2f;

            var targetDuration = getUpType == RecoveryGetUpType.Back
                ? _animConfig.getUpBackTargetDuration
                : _animConfig.getUpFrontTargetDuration;
            var poseMatchDuration = Mathf.Max(0f, _animConfig.recoveryPoseMatchDuration);
            var crossFadeDuration = Mathf.Max(0f, _animConfig.getUpCrossFadeDuration);
            return Mathf.Max(0.2f, targetDuration) + poseMatchDuration + crossFadeDuration + 0.8f;
        }
    }
}
