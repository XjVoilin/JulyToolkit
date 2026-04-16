using JulyCore;
using JulyCore.Provider.UI;
using UnityEngine;

namespace JulyToolkit
{
    /// <summary>
    /// 挂载到 CloseBtn 上，自动查找父级 UIBase 并在点击时关闭窗口。
    /// 无需每个窗口重复编写关闭逻辑。
    /// </summary>
    [RequireComponent(typeof(UISmartButton))]
    public class UICloseButton : MonoBehaviour
    {
        private UIBase _window;
        private UISmartButton _button;

        private void Awake()
        {
            _button = GetComponent<UISmartButton>();
            _window = GetComponentInParent<UIBase>();
            if (_window == null)
                GF.LogWarning($"[UICloseButton] 未找到父级 UIBase: {gameObject.name}");
            _button.onClick.AddListener(OnClick);
        }

        private void OnDestroy()
        {
            _button.onClick.RemoveListener(OnClick);
        }

        private void OnClick()
        {
            if (_window != null && _window.IsOpened)
                GF.UI.Close(_window, true);
        }
    }
}
