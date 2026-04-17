using DG.Tweening;
using UnityEngine;

namespace JulyToolkit
{
    /// <summary>
    /// 给任意 UI 元素添加上下浮动待机动画。
    /// - 挂在 RectTransform 上即可，不指定 target 时默认为自身。
    /// - 使用 DOTween Yoyo 无限循环，可配置是否忽略 timeScale。
    /// - OnEnable 开始，OnDisable / enabled=false 归位并 Kill。
    /// </summary>
    [DisallowMultipleComponent]
    public class UIFloatIdle : MonoBehaviour
    {
        [SerializeField] private RectTransform _target;

        [Tooltip("向上漂浮的振幅(像素)，元素会在基准位置和基准位置+振幅之间往返")]
        [SerializeField] private float _amplitude = 20f;

        [Tooltip("单次上浮/下沉时长(秒)，完整周期 = 2 * duration")]
        [SerializeField] private float _duration = 1f;

        [SerializeField] private Ease _ease = Ease.InOutSine;

        [Tooltip("忽略 timeScale，过场/暂停期间需要开启")]
        [SerializeField] private bool _ignoreTimeScale = true;

        private Vector2 _basePos;
        private Tween _tween;

        private void OnEnable()
        {
            if (_target == null) _target = GetComponent<RectTransform>();
            if (_target == null) return;

            _basePos = _target.anchoredPosition;
            _tween?.Kill();
            _tween = _target.DOAnchorPosY(_basePos.y + _amplitude, _duration)
                .SetEase(_ease)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(_ignoreTimeScale)
                .SetLink(gameObject);
        }

        private void OnDisable()
        {
            _tween?.Kill();
            _tween = null;
            if (_target != null) _target.anchoredPosition = _basePos;
        }
    }
}
