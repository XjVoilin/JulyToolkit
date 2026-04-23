using DG.Tweening;
using UnityEngine;

namespace JulyToolkit
{
    public struct PageTransitionContext
    {
        public RectTransform FromPage;
        public RectTransform ToPage;
        public Vector2 FromOriginalPosition;
        public Vector2 ToOriginalPosition;
        /// <summary>1 = forward (next), -1 = backward (prev)</summary>
        public int Direction;
        public float SlideDistance;
    }

    /// <summary>
    /// 翻页过渡效果基类。挂在 <see cref="UIPageNavigator"/> 同级或子级，
    /// 由 Navigator 序列化引用。<br/>
    /// 子类实现 <see cref="Play"/> 返回一个 Tween/Sequence，
    /// Navigator 负责在动画完成后处理页面显隐和状态刷新。
    /// </summary>
    public abstract class PageTransition : MonoBehaviour
    {
        /// <summary>
        /// 创建并返回过渡动画。Navigator 会自动为返回的 Tween 设置 SetLink 和 OnComplete。
        /// 实现时只需关注动画本身，不需要管页面显隐。
        /// </summary>
        public abstract Tween Play(PageTransitionContext ctx);
    }
}
