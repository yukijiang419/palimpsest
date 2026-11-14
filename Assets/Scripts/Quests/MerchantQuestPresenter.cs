using System;
using Caffeine.Gameplay.UI;
using Caffeine.Gameplay.Config;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Caffeine.Gameplay.Presentation;

namespace Caffeine.Gameplay.Quests
{
    [DefaultExecutionOrder(210)]
    public sealed class MerchantQuestPresenter : MonoBehaviour
    {
        private GameObject canvasObject;
        private RectTransform panel;
        private RectTransform trackerPanel;
        private TMP_Text tracker;
        private TMP_Text trackerHeading;
        private TMP_Text prompt;
        private RectTransform promptRoot;
        private PlayerHudPresenter playerHud;
        private TMP_Text body;
        private TypewriterText bodyTypewriter;
        private UnityEngine.UI.Button dialogueAdvance;
        private RectTransform options;
        private string[] lines;
        private int lineIndex;
        private Transform arrow;
        private Vector3? guidanceTarget;
        private GameplayConfig.QuestSettings markerSettings;
        private GameTimeController gameTime;
        public bool IsShowing => panel != null && panel.gameObject.activeSelf;
        private Action rewardContinue;
        private UnityEngine.UI.Button rewardConfirm;
        private int rewardOpenedFrame;
        public bool IsRewardShowing => rewardContinue != null;

        public void Initialize(GameplayConfig.QuestSettings settings, GameObject marker)
        {
            gameTime = FindFirstObjectByType<GameTimeController>();
            canvasObject = new GameObject("Quest UI", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            trackerPanel = ArchiveUi.Rect("Quest tracker", canvas.transform, new Vector2(0, 1),
                new Vector2(0, 1), new Vector2(32, -384), new Vector2(400, 106));
            ArchiveUi.Frame(trackerPanel, ArchiveUi.Copper);
            trackerHeading = ArchiveUi.Text(trackerPanel, "", new Vector2(24, -14), new Vector2(352, 28), 20);
            ArchiveUi.ApplyFont(trackerHeading, ArchiveUi.FontRole.Technical);
            trackerHeading.name = "Quest progress";
            trackerHeading.color = new Color(0.72f, 0.75f, 0.73f);
            tracker = ArchiveUi.Text(trackerPanel, "", new Vector2(24, -48), new Vector2(352, 38), 24);
            tracker.name = "Quest status";
            trackerPanel.gameObject.SetActive(false);
            promptRoot = ArchiveUi.Prompt(canvas.transform, "E", Vector2.zero, new Vector2(260, 62), out prompt);
            ArchiveUi.Place(promptRoot, new Vector2(.5f, .5f), new Vector2(0, .5f), Vector2.zero, new Vector2(260, 62));
            playerHud = FindFirstObjectByType<PlayerHudPresenter>();
            prompt.text = "Talk";
            promptRoot.gameObject.SetActive(false);
            markerSettings = settings;
            if (marker == null)
            {
                Debug.LogError("Merchant is missing its quest arrow object. Dialogue remains available.", this);
                return;
            }
            arrow = marker.transform;
            marker.SetActive(false);
        }

        public void SetGuidance(string heading, string status, Vector3? target, bool visible, bool canTalk)
        {
            if (canvasObject == null)
            {
                return;
            }

            trackerHeading.text = heading;
            visible &= gameTime == null || !gameTime.IsQuestOverviewPaused;
            tracker.text = status;
            trackerPanel.gameObject.SetActive(visible && !IsShowing && status.Length > 0);
            promptRoot.gameObject.SetActive(visible && !IsShowing && canTalk);
            guidanceTarget = target;
            if (arrow != null)
            {
                arrow.gameObject.SetActive(visible && !IsShowing && target.HasValue);
            }
        }

        private void LateUpdate()
        {
            if (gameTime != null && gameTime.IsQuestOverviewPaused)
            {
                trackerPanel.gameObject.SetActive(false);
                promptRoot.gameObject.SetActive(false);
                if (arrow != null) arrow.gameObject.SetActive(false);
                return;
            }
            if (IsRewardShowing && Time.frameCount > rewardOpenedFrame && Keyboard.current != null
                && Keyboard.current.enterKey.wasPressedThisFrame) rewardConfirm.onClick.Invoke();
            if (promptRoot != null && promptRoot.gameObject.activeSelf && playerHud != null)
            {
                if (playerHud.TryGetStaminaScreenPoint(out var screenPoint))
                {
                    var canvas = canvasObject.GetComponent<Canvas>();
                    screenPoint.x += 18f * canvas.scaleFactor;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform,
                        screenPoint, null, out var point);
                    promptRoot.anchoredPosition = point;
                }
                else promptRoot.gameObject.SetActive(false);
            }
            if (arrow == null || !arrow.gameObject.activeSelf || !guidanceTarget.HasValue)
            {
                return;
            }

            float bob = Mathf.Sin(Time.time * markerSettings.ArrowFloatSpeed) * markerSettings.ArrowFloatAmplitude;
            arrow.position = guidanceTarget.Value + Vector3.up * (markerSettings.ArrowHeight + bob);
            arrow.Rotate(Vector3.up, markerSettings.ArrowRotateSpeed * Time.deltaTime, Space.World);
        }

        public void Show(MerchantQuestDefinition definition, QuestStage stage, bool canAccept,
            Action accept, Action turnIn, Action shop, Action close)
        {
            Close();
            panel = ArchiveUi.Rect("Merchant dialogue", canvasObject.transform, new Vector2(0.5f, 0),
                new Vector2(0.5f, 0), new Vector2(0, 45), new Vector2(1540, 340));
            ArchiveUi.Frame(panel, ArchiveUi.Copper);
            var background = panel.GetComponent<UnityEngine.UI.Image>();
            background.raycastTarget = true;
            dialogueAdvance = panel.gameObject.AddComponent<UnityEngine.UI.Button>();
            dialogueAdvance.targetGraphic = background;
            dialogueAdvance.transition = UnityEngine.UI.Selectable.Transition.None;
            dialogueAdvance.onClick.AddListener(Advance);
            ArchiveUi.Crest(panel, new Vector2(28, 35), 132);
            var nameplate = ArchiveUi.Rect("Speaker nameplate", panel, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(172, 28), new Vector2(430, 64));
            ArchiveUi.Surface(nameplate, "button");
            ArchiveUi.Text(nameplate, definition.speaker, new Vector2(28, 0), new Vector2(374, 64), 30,
                fontRole: ArchiveUi.FontRole.Heading);
            body = ArchiveUi.Text(panel,
                "",
                new Vector2(64, -104),
                new Vector2(1412, 116),
                29,
                TextAlignmentOptions.TopLeft);
            body.textWrappingMode = TextWrappingModes.Normal;
            body.lineSpacing = 6;
            bodyTypewriter = body.gameObject.AddComponent<TypewriterText>();
            lines = stage switch
            {
                QuestStage.Available => definition.offer,
                QuestStage.Travelling => definition.inProgress,
                QuestStage.ReturnToMerchant => definition.readyToTurnIn,
                _ => definition.completed
            };
            lineIndex = 0;
            options = ArchiveUi.Rect("Dialogue choices", panel, new Vector2(0.5f, 0),
                new Vector2(0.5f, 0), new Vector2(0, 28), new Vector2(1360, 62));
            bool hasTaskChoice = stage == QuestStage.Available && canAccept || stage == QuestStage.ReturnToMerchant;
            if (stage == QuestStage.Available && canAccept)
            {
                AddChoice("Accept task", -420, accept, ArchiveUi.Cyan);
            }
            else if (stage == QuestStage.ReturnToMerchant)
            {
                AddChoice(definition.HasPuzzle ? "Submit record" : "Collect reward",
                    -420, turnIn, ArchiveUi.Cyan);
            }

            AddChoice("Open shop", hasTaskChoice ? 0 : -210, shop, ArchiveUi.Copper);
            AddChoice("Leave", hasTaskChoice ? 420 : 210, close, ArchiveUi.Copper);
            RefreshLine();
        }

        private void AddChoice(string title, float x, Action action, Color accent)
        {
            var button = ArchiveUi.Button(options, title, 0, () => action(), accent);
            var rect = (RectTransform)button.transform;
            rect.anchoredPosition = new Vector2(x, 0);
            rect.sizeDelta = new Vector2(360, 62);
            button.GetComponentInChildren<TMP_Text>().rectTransform.sizeDelta = rect.sizeDelta;
        }

        private void Advance()
        {
            if (bodyTypewriter != null && bodyTypewriter.Complete()) return;
            if (lines == null || lineIndex >= lines.Length - 1) return;
            lineIndex++;
            RefreshLine();
        }

        private void RefreshLine()
        {
            float speed = ConfigSystem.Instance != null ? ConfigSystem.Instance.Config.Story.TypewriterCharactersPerSecond : 35f;
            bodyTypewriter.Play(lines != null && lines.Length > 0 ? lines[Mathf.Min(lineIndex, lines.Length - 1)] : "...",
                speed, RefreshDialogueControls);
            RefreshDialogueControls();
        }

        private void RefreshDialogueControls()
        {
            bool last = lines == null || lines.Length == 0 || lineIndex >= lines.Length - 1;
            bool playing = bodyTypewriter != null && bodyTypewriter.IsPlaying;
            dialogueAdvance.interactable = playing || !last;
            options.gameObject.SetActive(last && !playing);
            ArchiveUi.Focus(last && !playing ? options.GetComponentInChildren<UnityEngine.UI.Button>() : dialogueAdvance);
        }

        public void Close()
        {
            if (bodyTypewriter != null) bodyTypewriter.Cancel();
            bodyTypewriter = null;
            rewardContinue = null;
            if (panel == null)
            {
                return;
            }

            panel.gameObject.SetActive(false);
            Destroy(panel.gameObject);
            panel = null;
        }

        public void ShowWeaponReward(WeaponData weapon, Action continueAction)
        {
            Close();
            rewardContinue = continueAction;
            rewardOpenedFrame = Time.frameCount;
            panel = ArchiveUi.Stretch("Weapon acquired", canvasObject.transform);
            ArchiveUi.Paint(panel, new Color(0f, 0f, 0f, .78f));
            var card = ArchiveUi.Rect("Weapon reward card", panel, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                Vector2.zero, new Vector2(820f, 560f));
            ArchiveUi.Frame(card, ArchiveUi.Copper);
            ArchiveUi.Text(card, "WEAPON ACQUIRED", new Vector2(30f, -25f), new Vector2(760f, 48f), 34f,
                TextAlignmentOptions.Center, ArchiveUi.FontRole.Heading);
            var icon = ArchiveUi.Rect("Reward icon", card, new Vector2(.5f, 1f), new Vector2(.5f, 1f),
                new Vector2(0f, -88f), new Vector2(240f, 190f));
            var image = icon.gameObject.AddComponent<Image>();
            image.sprite = weapon.icon; image.preserveAspect = true; image.raycastTarget = false;
            ArchiveUi.Text(card, weapon.weaponName, new Vector2(30f, -290f), new Vector2(760f, 44f), 32f, TextAlignmentOptions.Center);
            ArchiveUi.Text(card, $"Damage {weapon.damage:0}  ·  Range {weapon.range:0.#} m\nEquipped  ·  [{weapon.slot}] Weapon slot",
                new Vector2(30f, -350f), new Vector2(760f, 90f), 27f, TextAlignmentOptions.Center);
            var confirm = ArchiveUi.Button(card, "Continue · Enter", -465f, ConfirmReward, ArchiveUi.Cyan);
            rewardConfirm = confirm;
            ArchiveUi.Focus(confirm);
            FeedbackAudioController.Play(FeedbackSound.Open);
        }

        private void ConfirmReward()
        {
            var action = rewardContinue;
            if (action == null) return;
            FeedbackAudioController.Play(FeedbackSound.Close);
            Close();
            action();
        }

        private void OnDestroy()
        {
            if (canvasObject != null)
            {
                Destroy(canvasObject);
            }

            if (arrow != null)
            {
                arrow.gameObject.SetActive(false);
            }
        }
    }
}
