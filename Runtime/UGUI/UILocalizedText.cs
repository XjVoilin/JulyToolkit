using JulyCore;
using TMPro;
using UnityEngine;

namespace JulyToolkit
{
    /// <summary>
    /// 挂在带有 TMP 的物体上，填入 Key 即可自动本地化。
    /// 支持格式化参数，语言切换时自动刷新。
    /// </summary>
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class UILocalizedText : MonoBehaviour
    {
        [SerializeField] private string key;

        private TextMeshProUGUI _text;
        private object[] _args;

        private void Awake()
        {
            _text = GetComponent<TextMeshProUGUI>();
        }

        private void OnEnable()
        {
            GF.Event.Subscribe<LanguageChangedEvent>(OnLanguageChanged, this);
            Apply();
        }

        private void OnDisable()
        {
            GF.Event.UnsubscribeAll(this);
        }

        public void SetKey(string newKey)
        {
            key = newKey;
            _args = null;
            Apply();
        }

        public void SetKey(string newKey, params object[] args)
        {
            key = newKey;
            _args = args is { Length: > 0 } ? args : null;
            Apply();
        }

        private void Apply()
        {
            if (_text == null || string.IsNullOrEmpty(key)) return;

            _text.text = _args != null
                ? GF.Localization.GetFormat(key, _args)
                : GF.Localization.Get(key);
        }

        private void OnLanguageChanged(LanguageChangedEvent e)
        {
            Apply();
        }
    }
}
