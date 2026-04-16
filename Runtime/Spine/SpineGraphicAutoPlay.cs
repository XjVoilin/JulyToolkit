using Spine.Unity;
using UnityEngine;
using AnimationState = Spine.AnimationState;

namespace JulyToolkit.Spine
{
    [RequireComponent(typeof(SkeletonGraphic))]
    public class SpineGraphicAutoPlay : SpineAutoPlayBase
    {
        private SkeletonGraphic _skeletonGraphic;

        private void Awake()
        {
            _skeletonGraphic = GetComponent<SkeletonGraphic>();
        }

        protected override AnimationState GetAnimationState()
        {
            return _skeletonGraphic != null ? _skeletonGraphic.AnimationState : null;
        }
    }
}
