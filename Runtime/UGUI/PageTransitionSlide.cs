using DG.Tweening;
using UnityEngine;

namespace JulyToolkit
{
    /// <summary>
    /// 水平滑动过渡：当前页滑出 + 新页滑入，可选缩放效果。
    /// </summary>
    [DisallowMultipleComponent]
    public class PageTransitionSlide : PageTransition
    {
        [SerializeField] private float _duration = 0.3f;
        [SerializeField] private Ease _ease = Ease.OutCubic;
        [SerializeField] private bool _enableScale;
        [Tooltip("缩放起始值，仅 enableScale 为 true 时生效")]
        [SerializeField] private float _scaleFrom = 0.85f;

        public override Tween Play(PageTransitionContext ctx)
        {
            var fromPage = ctx.FromPage;
            var toPage = ctx.ToPage;
            var dir = ctx.Direction;
            var dist = ctx.SlideDistance;
            var fromPos = ctx.FromOriginalPosition;
            var toPos = ctx.ToOriginalPosition;

            toPage.anchoredPosition = new Vector2(toPos.x + dir * dist, toPos.y);

            if (_enableScale)
                toPage.localScale = Vector3.one * _scaleFrom;

            var seq = DOTween.Sequence();

            seq.Join(fromPage.DOAnchorPosX(fromPos.x - dir * dist, _duration).SetEase(_ease));
            seq.Join(toPage.DOAnchorPosX(toPos.x, _duration).SetEase(_ease));

            if (_enableScale)
            {
                seq.Join(fromPage.DOScale(_scaleFrom, _duration).SetEase(_ease));
                seq.Join(toPage.DOScale(1f, _duration).SetEase(_ease));
            }

            return seq;
        }
    }
}
