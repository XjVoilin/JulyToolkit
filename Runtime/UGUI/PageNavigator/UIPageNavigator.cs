using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;

namespace JulyToolkit
{
    /// <summary>
    /// 通用翻页导航器：管理翻页按钮、页码圆点和页面过渡动画。
    /// 页面内容由外部配置，组件只负责显隐和过渡。
    /// 可继承覆写 <see cref="OnCloseClicked"/> 自定义关闭逻辑，
    /// 覆写 <see cref="UpdateButtonStates"/> 控制按钮显隐。
    /// 过渡效果通过 <see cref="PageTransition"/> 策略组件可插拔替换。
    /// </summary>
    [DisallowMultipleComponent]
    public class UIPageNavigator : MonoBehaviour
    {
        [Header("页面")]
        [SerializeField] private List<RectTransform> _pages = new();

        [Header("导航按钮")]
        [SerializeField] private UISmartButton _btnPrev;
        [SerializeField] private UISmartButton _btnNext;
        [SerializeField] protected UISmartButton _btnClose;

        [Header("页码指示器")]
        [SerializeField] private RectTransform _indicatorRoot;
        [SerializeField] private UIPageDot _dotTemplate;

        [Header("过渡动画")]
        [SerializeField] private List<PageTransition> _transitions = new();
        [Tooltip("0 = 自动取自身 RectTransform 宽度")]
        [SerializeField] private float _slideDistance;

        [Header("选项")]
        [SerializeField] private bool _loop;
        [SerializeField] private int _startPage;

        [Header("事件")]
        public UnityEvent<int> onPageChanged = new();
        public UnityEvent onClose = new();

        private int _currentPage;
        private bool _isTransitioning;
        private Tween _activeTween;
        private readonly List<UIPageDot> _dots = new();

        public int PageCount => _pages.Count;
        public int CurrentPage => _currentPage;
        public bool IsTransitioning => _isTransitioning;

        private void Awake()
        {
            InitDots();
            BindButtons();
            _currentPage = Mathf.Clamp(_startPage, 0, Mathf.Max(0, _pages.Count - 1));
            ShowPageImmediate(_currentPage);
        }

        private void OnDestroy()
        {
            UnbindButtons();
            _activeTween?.Kill();
        }

        #region Public API

        public void GoToPage(int index)
        {
            if (_isTransitioning || _pages.Count <= 1) return;
            var resolved = ResolveIndex(index);
            if (resolved < 0 || resolved == _currentPage) return;

            var dir = resolved > _currentPage ? PageDirection.Forward : PageDirection.Backward;
            if (_loop)
            {
                if (_currentPage == 0 && resolved == _pages.Count - 1) dir = PageDirection.Backward;
                else if (_currentPage == _pages.Count - 1 && resolved == 0) dir = PageDirection.Forward;
            }
            DoTransition(resolved, dir);
        }

        public void NextPage() => Step(PageDirection.Forward);
        public void PrevPage() => Step(PageDirection.Backward);

        private void Step(PageDirection direction)
        {
            if (_isTransitioning || _pages.Count <= 1) return;
            var resolved = ResolveIndex(_currentPage + (int)direction);
            if (resolved >= 0 && resolved != _currentPage)
                DoTransition(resolved, direction);
        }

        private int ResolveIndex(int index)
        {
            if (_pages.Count == 0) return -1;
            if (_loop)
                return ((index % _pages.Count) + _pages.Count) % _pages.Count;
            return index >= 0 && index < _pages.Count ? index : -1;
        }

        #endregion

        #region Virtual

        protected virtual void OnCloseClicked()
        {
            onClose?.Invoke();
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 每次翻页后调用。基类默认：非循环模式首页禁用上一页、末页禁用下一页，关闭按钮始终显示。
        /// 子类覆写可实现如"关闭按钮仅末页显示"等自定义逻辑。
        /// </summary>
        protected virtual void UpdateButtonStates()
        {
            if (_loop)
            {
                if (_btnPrev) _btnPrev.SetInteractable(true);
                if (_btnNext) _btnNext.SetInteractable(true);
                return;
            }
            if (_btnPrev) _btnPrev.SetInteractable(_currentPage > 0);
            if (_btnNext) _btnNext.SetInteractable(_currentPage < _pages.Count - 1);
        }

        #endregion

        #region Internal

        private void InitDots()
        {
            if (!_dotTemplate || !_indicatorRoot) return;
            _dotTemplate.gameObject.SetActive(false);

            for (var i = 0; i < _pages.Count; i++)
            {
                var dot = Instantiate(_dotTemplate, _indicatorRoot);
                dot.gameObject.SetActive(true);
                _dots.Add(dot);
            }
        }

        private void RefreshDots()
        {
            for (var i = 0; i < _dots.Count; i++)
                if (_dots[i]) _dots[i].SetSelected(i == _currentPage);
        }

        private void BindButtons()
        {
            if (_btnPrev) _btnPrev.onClick.AddListener(PrevPage);
            if (_btnNext) _btnNext.onClick.AddListener(NextPage);
            if (_btnClose) _btnClose.onClick.AddListener(OnCloseClicked);
        }

        private void UnbindButtons()
        {
            if (_btnPrev) _btnPrev.onClick.RemoveListener(PrevPage);
            if (_btnNext) _btnNext.onClick.RemoveListener(NextPage);
            if (_btnClose) _btnClose.onClick.RemoveListener(OnCloseClicked);
        }

        private void ShowPageImmediate(int index)
        {
            for (var i = 0; i < _pages.Count; i++)
            {
                if (!_pages[i]) continue;
                _pages[i].gameObject.SetActive(i == index);
                _pages[i].localScale = Vector3.one;
            }
            _currentPage = index;
            UpdateButtonStates();
            RefreshDots();
        }

        private float GetSlideDistance()
        {
            if (_slideDistance > 0) return _slideDistance;
            var rt = GetComponent<RectTransform>();
            return rt ? rt.rect.width : 800f;
        }

        private void DoTransition(int toIndex, PageDirection direction)
        {
            _isTransitioning = true;
            _activeTween?.Kill();

            var fromPage = _pages[_currentPage];
            var toPage = _pages[toIndex];
            var fromPos = fromPage.anchoredPosition;

            toPage.gameObject.SetActive(true);

            if (_transitions.Count > 0)
            {
                var toOrigPos = toPage.anchoredPosition;
                var done = false;

                void Finish()
                {
                    if (done) return;
                    done = true;
                    toPage.anchoredPosition = toOrigPos;
                    toPage.localScale = Vector3.one;
                    CompleteTransition(fromPage, fromPos, toIndex);
                }

                var ctx = new PageTransitionContext
                {
                    FromPage = fromPage,
                    ToPage = toPage,
                    FromOriginalPosition = fromPos,
                    ToOriginalPosition = toOrigPos,
                    Direction = direction,
                    SlideDistance = GetSlideDistance()
                };

                var seq = DOTween.Sequence();
                foreach (var t in _transitions)
                    if (t) seq.Join(t.Play(ctx));

                _activeTween = seq
                    .SetUpdate(true)
                    .SetLink(gameObject)
                    .OnComplete(Finish)
                    .OnKill(Finish);
            }
            else
            {
                CompleteTransition(fromPage, fromPos, toIndex);
            }
        }

        private void CompleteTransition(RectTransform fromPage, Vector2 fromOrigPos, int toIndex)
        {
            fromPage.gameObject.SetActive(false);
            fromPage.anchoredPosition = fromOrigPos;
            fromPage.localScale = Vector3.one;

            _currentPage = toIndex;
            _isTransitioning = false;
            _activeTween = null;

            UpdateButtonStates();
            RefreshDots();
            onPageChanged?.Invoke(_currentPage);
        }

        #endregion
    }
}
