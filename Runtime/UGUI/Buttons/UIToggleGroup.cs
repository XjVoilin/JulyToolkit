using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace JulyToolkit
{
    public class UIToggleGroup : MonoBehaviour
    {
        [SerializeField] private int m_SelectedIndex;
        [SerializeField] private List<UIToggleItem> m_Items = new();
        [SerializeField] private UnityEvent<int> m_OnValueChanged = new();

        public int SelectedIndex
        {
            get => m_SelectedIndex;
            set
            {
                if (value < 0 || value >= m_Items.Count) return;
                if (m_SelectedIndex == value) return;
                ApplySelection(value);
                m_OnValueChanged.Invoke(m_SelectedIndex);
            }
        }

        public int Count => m_Items.Count;

        public UnityEvent<int> OnValueChanged => m_OnValueChanged;

        public UIToggleItem GetItem(int index) => m_Items[index];

        public void SetWithoutNotify(int index)
        {
            if (index < 0 || index >= m_Items.Count) return;
            if (m_SelectedIndex == index) return;
            ApplySelection(index);
        }

        internal void NotifyItemClicked(UIToggleItem item)
        {
            int index = m_Items.IndexOf(item);
            if (index < 0 || m_SelectedIndex == index) return;
            ApplySelection(index);
            m_OnValueChanged.Invoke(m_SelectedIndex);
        }

        private void ApplySelection(int index)
        {
            for (int i = 0; i < m_Items.Count; i++)
            {
                if (m_Items[i] != null)
                    m_Items[i].SetOn(i == index);
            }
            m_SelectedIndex = index;
        }

        private void OnEnable()
        {
            if (m_Items.Count > 0)
            {
                m_SelectedIndex = Mathf.Clamp(m_SelectedIndex, 0, m_Items.Count - 1);
                ApplySelection(m_SelectedIndex);
            }
        }
    }
}
