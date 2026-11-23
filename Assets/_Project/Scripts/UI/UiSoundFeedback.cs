using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BORis
{
    [RequireComponent(typeof(Selectable))]
    public sealed class UiSoundFeedback : MonoBehaviour, IPointerEnterHandler, IPointerDownHandler, ISubmitHandler
    {
        Selectable control;
        void Awake() => control = GetComponent<Selectable>();
        bool Available => control != null && control.IsActive() && control.IsInteractable();
        public void OnPointerEnter(PointerEventData data)
        { if (Available) WorkshopAudio.Play(WorkshopSound.UiHover, .08f); }
        public void OnPointerDown(PointerEventData data)
        { if (Available && data.button == PointerEventData.InputButton.Left) WorkshopAudio.Play(WorkshopSound.UiClick, .18f); }
        public void OnSubmit(BaseEventData data)
        { if (Available) WorkshopAudio.Play(WorkshopSound.UiClick, .18f); }
    }
}
