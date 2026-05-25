using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JulyToolkit
{
    public class UIToggleItem : Selectable, IPointerClickHandler
    {
        [SerializeField] private GameObject m_Normal;
        [SerializeField] private GameObject m_Selected;

        private UIToggleGroup m_Group;
        private bool m_IsOn;

        public bool IsOn => m_IsOn;

        public virtual void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button > 0) return;
            if (!IsActive() || !IsInteractable()) return;
            if (m_Group != null) m_Group.NotifyItemClicked(this);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            m_Group = GetComponentInParent<UIToggleGroup>();
            UpdateVisuals();
        }

        internal void SetOn(bool value)
        {
            if (m_IsOn == value) return;
            m_IsOn = value;
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            if (m_Normal != null) m_Normal.SetActive(!m_IsOn);
            if (m_Selected != null) m_Selected.SetActive(m_IsOn);
        }
    }
}
