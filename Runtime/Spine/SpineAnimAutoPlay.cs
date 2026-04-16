using Spine.Unity;
using UnityEngine;
using AnimationState = Spine.AnimationState;

namespace JulyToolkit.Spine
{
    [RequireComponent(typeof(SkeletonAnimation))]
    public class SpineAnimAutoPlay : SpineAutoPlayBase
    {
        private SkeletonAnimation _skeletonAnimation;

        private void Awake()
        {
            _skeletonAnimation = GetComponent<SkeletonAnimation>();
        }

        protected override AnimationState GetAnimationState()
        {
            return _skeletonAnimation != null ? _skeletonAnimation.AnimationState : null;
        }
    }
}
