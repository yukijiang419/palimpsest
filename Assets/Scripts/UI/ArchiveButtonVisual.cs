using TMPro;
using Caffeine.Gameplay.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace Caffeine.Gameplay.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class ArchiveButtonVisual : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, IPointerClickHandler, ISelectHandler, IDeselectHandler, IMoveHandler
    {
        [SerializeField] private bool card;
        private Button button;
        private Image face;
        private TMP_Text label;
        private RectTransform cardFocus;
        private bool hovered, selected, pressed;
        private bool rejectedPress;
        private int displayed = -1;
        private static bool mouseMode;
        private static int inputFrame = -1;
        private static Vector2 lastPointer;
        private static bool pointerKnown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInputMode() { mouseMode = pointerKnown = false; inputFrame = -1; }

        private static void UpdateInputMode()
        {
            if (inputFrame == Time.frameCount) return;
            inputFrame = Time.frameCount;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                var position = mouse.position.ReadValue();
                if (pointerKnown && (position - lastPointer).sqrMagnitude > .25f || mouse.leftButton.wasPressedThisFrame)
                    mouseMode = true;
                lastPointer = position;
                pointerKnown = true;
            }
            var keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.upArrowKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame
                || keyboard.leftArrowKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame
                || keyboard.tabKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame
                || keyboard.spaceKey.wasPressedThisFrame)) mouseMode = false;
            var pad = Gamepad.current;
            if (pad != null && (pad.dpad.ReadValue().sqrMagnitude > .1f || pad.leftStick.ReadValue().sqrMagnitude > .25f
                || pad.buttonSouth.wasPressedThisFrame)) mouseMode = false;
        }

        public void Configure(bool isCard)
        {
            card = isCard;
            button = GetComponent<Button>();
            button.onClick.RemoveListener(PlayConfirmation);
            button.onClick.AddListener(PlayConfirmation);
            face = GetComponent<Image>();
            label = GetComponentInChildren<TMP_Text>();
            button.transition = Selectable.Transition.None;
            selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
            if (card)
            {
                cardFocus = transform.Find("Selection rim") as RectTransform;
                if (cardFocus == null)
                {
                    cardFocus = ArchiveUi.Stretch("Selection rim", transform);
                    cardFocus.offsetMin = Vector2.one * 7;
                    cardFocus.offsetMax = Vector2.one * -7;
                    ArchiveUi.Outline(cardFocus, ArchiveUi.Cyan);
                }
            }
            displayed = -1;
            Refresh();
        }

        private void OnEnable() => Configure(card);
        private void LateUpdate() { UpdateInputMode(); Refresh(); }
        private void Refresh()
        {
            if (button == null || face == null) return;
            int state = !button.interactable ? 3 : pressed ? 2 : (mouseMode ? hovered : selected) ? 1 : 0;
            if (displayed == state) return;
            displayed = state;
            face.sprite = ArchiveUi.Sprite(card ? "panel" : state == 1 || state == 2 ? "button-active" : "button");
            face.color = state == 3 ? new Color(.48f, .52f, .5f, .8f)
                : state == 2 ? new Color(.82f, .82f, .82f) : card && state == 0 ? new Color(.78f, .85f, .82f) : Color.white;
            if (cardFocus != null) cardFocus.gameObject.SetActive(state == 1 || state == 2);
            if (!card && label != null) label.color = state == 1 || state == 2 ? ArchiveUi.Ink : ArchiveUi.Paper;
        }
        public void OnPointerEnter(PointerEventData data)
        {
            hovered = true;
            if (data.delta.sqrMagnitude > .25f)
            {
                mouseMode = true;
                if (button.IsInteractable()) FeedbackAudioController.Play(FeedbackSound.Navigate);
            }
            Refresh();
        }
        public void OnPointerExit(PointerEventData _) { hovered = false; pressed = rejectedPress = false; Refresh(); }
        public void OnPointerDown(PointerEventData data)
        {
            mouseMode = true;
            pressed = true;
            rejectedPress = card && !button.interactable && data.button == PointerEventData.InputButton.Left;
            Refresh();
        }
        public void OnPointerUp(PointerEventData _) { pressed = false; Refresh(); }
        public void OnSelect(BaseEventData data)
        {
            selected = true;
            if (data is AxisEventData move && move.moveDir != MoveDirection.None && button.IsInteractable())
            {
                FeedbackAudioController.Play(FeedbackSound.Navigate);
            }
            Refresh();
        }
        private void PlayConfirmation() => FeedbackAudioController.Play(FeedbackSound.Confirm);
        public void OnPointerClick(PointerEventData data)
        {
            if (rejectedPress && data.button == PointerEventData.InputButton.Left)
            {
                FeedbackAudioController.Play(FeedbackSound.Reject);
            }
            rejectedPress = false;
        }
        public void OnDeselect(BaseEventData _) { selected = false; Refresh(); }
        public void OnMove(AxisEventData _) { mouseMode = false; Refresh(); }
        private void OnDisable() { hovered = selected = pressed = rejectedPress = false; displayed = -1; }
    }
}
