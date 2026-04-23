using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;

namespace JulyToolkit
{
    /// <summary>
    /// 通用翻页导航器：管理上一页/下一页/关闭按钮、页码圆点指示器和页面切换动画。
    /// 页面内容由外部配置（Image、Spine 等均可），组件只负责显隐和过渡动画。
    /// 项目组可继承并覆写 <see cref="OnCloseClicked"/> 实现自定义关闭逻辑。
    /// 过渡效果通过 <see cref="PageTransition"/> 策略组件配置，可随时替换。
    /// </summary>
    [DisallowMultipleComponent]
    public class UIPageNavigator : MonoBehaviour
    {
        [Header("Pages")]
        [SerializeField] private List<RectTransform> _pages = new();

        [Header("Navigation")]
        [SerializeField] private UISmartButton _btnPrev;
        [SerializeField] private UISmartButton _btnNext;
        [SerializeField] private UISmartButton _btnClose;

        [Header("Indicator")]
        [Tooltip("圆点父节点（建议挂 HorizontalLayoutGroup）")]
        [SerializeField] private RectTransform _indicatorRoot;
        [Tooltip("圆点模板：运行时会被复制，模板自身会被隐藏")]
        [SerializeField] private UIPageDot _dotTemplate;

        [Header("Transition")]
        [Tooltip("过渡效果组件，为空时直接切换无动画")]
        [SerializeField] private PageTransition _transition;
        [Tooltip("滑动距离（像素），0 = 自动取自身 RectTransform 宽度")]
        [SerializeField] private float _slideDistance;

        [Header("Options")]
        [SerializeField] private bool _loop;
        [SerializeField] private int _startPage;

        [Header("Events")]
        public UnityEvent<int> onPageChanged = new();
        public UnityEvent onClose = new();

        private int _currentPage;
        private bool _isTransitioning;
        private Tween _activeTween;
        private readonly List<UIPageDot> _dots = new();
        private readonly List<Vector2> _cachedPositions = new();

        public int PageCount => _pages.Count;
        public int CurrentPage => _currentPage;
        public bool IsTransitioning => _isTransitioning;

        #region Lifecycle

        private void Awake()
        {
            CachePagePositions();
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

        #endregion

        #region Public API

        public void GoToPage(int index)
        {
            if (_isTransitioning || _pages.Count == 0) return;

            index = WrapOrClamp(index);
            if (index == _currentPage) return;

            int direction = index > _currentPage ? 1 : -1;
            if (_loop && _currentPage == 0 && index == _pages.Count - 1) direction = -1;
            if (_loop && _currentPage == _pages.Count - 1 && index == 0) direction = 1;

            DoTransition(_currentPage, index, direction);
        }

        public void NextPage()
        {
            if (_isTransitioning || _pages.Count <= 1) return;

            int next = _currentPage + 1;
            if (next >= _pages.Count)
            {
                if (_loop) next = 0;
                else return;
            }

            DoTransition(_currentPage, next, 1);
        }

        public void PrevPage()
        {
            if (_isTransitioning || _pages.Count <= 1) return;

            int prev = _currentPage - 1;
            if (prev < 0)
            {
                if (_loop) prev = _pages.Count - 1;
                else return;
            }

            DoTransition(_currentPage, prev, -1);
        }

        #endregion

        #region Virtual

        protected virtual void OnCloseClicked()
        {
            onClose?.Invoke();
        }

        #endregion

        #region Internal

        private void CachePagePositions()
        {
            _cachedPositions.Clear();
            foreach (var page in _pages)
                _cachedPositions.Add(page ? page.anchoredPosition : Vector2.zero);
        }

        private void InitDots()
        {
            if (_dotTemplate == null || _indicatorRoot == null) return;

            _dotTemplate.gameObject.SetActive(false);

            foreach (var dot in _dots)
                if (dot) Destroy(dot.gameObject);
            _dots.Clear();

            for (int i = 0; i < _pages.Count; i++)
            {
                var dot = Instantiate(_dotTemplate, _indicatorRoot);
                dot.gameObject.SetActive(true);
                _dots.Add(dot);
            }

            RefreshDots();
        }

        private void RefreshDots()
        {
            for (int i = 0; i < _dots.Count; i++)
            {
                if (_dots[i]) _dots[i].SetSelected(i == _currentPage);
            }
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

        private void UpdateButtonStates()
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

        private void ShowPageImmediate(int index)
        {
            for (int i = 0; i < _pages.Count; i++)
            {
                if (!_pages[i]) continue;
                _pages[i].gameObject.SetActive(i == index);
                _pages[i].anchoredPosition = _cachedPositions[i];
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

        private void DoTransition(int fromIndex, int toIndex, int direction)
        {
            _isTransitioning = true;
            _activeTween?.Kill();

            var fromPage = _pages[fromIndex];
            var toPage = _pages[toIndex];
            var fromPos = _cachedPositions[fromIndex];
            var toPos = _cachedPositions[toIndex];

            toPage.gameObject.SetActive(true);

            if (_transition)
            {
                var ctx = new PageTransitionContext
                {
                    FromPage = fromPage,
                    ToPage = toPage,
                    FromOriginalPosition = fromPos,
                    ToOriginalPosition = toPos,
                    Direction = direction,
                    SlideDistance = GetSlideDistance()
                };

                _activeTween = _transition.Play(ctx);
                _activeTween
                    .SetUpdate(true)
                    .SetLink(gameObject)
                    .OnComplete(() => OnTransitionComplete(fromIndex, toIndex, fromPos));
            }
            else
            {
                OnTransitionComplete(fromIndex, toIndex, fromPos);
            }
        }

        private void OnTransitionComplete(int fromIndex, int toIndex, Vector2 fromOrigPos)
        {
            var fromPage = _pages[fromIndex];
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

        private int WrapOrClamp(int index)
        {
            if (_pages.Count == 0) return 0;
            if (_loop)
                return ((index % _pages.Count) + _pages.Count) % _pages.Count;
            return Mathf.Clamp(index, 0, _pages.Count - 1);
        }

        #endregion
    }
}
