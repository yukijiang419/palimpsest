using Caffeine.Gameplay.Player;
using Caffeine.Gameplay.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Caffeine.Gameplay.Puzzles
{
    // Top-of-screen guidance shows the current timeline, next goal and short action feedback.
    public sealed class PuzzleHintPresenter : MonoBehaviour
    {
        private const float FlashDuration = 2.5f;
        private static readonly Color PastColor = new(1f, .57f, .26f);
        private static readonly Color PresentColor = new(.35f, .67f, 1f);
        private static readonly Color FutureColor = new(.68f, .43f, 1f);

        private TimelinePuzzleArea area;
        private PlayerInputReader input;
        private CanvasGroup group;
        private Image eraChip;
        private TMP_Text eraText;
        private TMP_Text goalText;
        private RectTransform panel;
        private TMP_Text feedbackText;
        private string flash;
        private float flashUntil;

        public static PuzzleHintPresenter Create(TimelinePuzzleArea area)
        {
            var canvasObject = new GameObject("Puzzle hints", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var hints = canvasObject.AddComponent<PuzzleHintPresenter>();
            hints.area = area;
            hints.Build(canvas.transform);
            return hints;
        }

        // Short feedback such as a cancelled timeline shift temporarily expands the compact goal panel.
        public void Flash(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            flash = message;
            flashUntil = Time.unscaledTime + FlashDuration;
        }

        private void Build(Transform root)
        {
            group = gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            panel = ArchiveUi.Rect("Puzzle hint panel", root, new Vector2(.5f, 1), new Vector2(.5f, 1),
                new Vector2(0, -24), new Vector2(920, 68));
            ArchiveUi.Frame(panel, ArchiveUi.Copper);
            var chip = ArchiveUi.Rect("Timeline", panel, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(18, -14), new Vector2(150, 38));
            eraChip = ArchiveUi.Surface(chip, "button");
            eraText = ArchiveUi.Text(chip, "", Vector2.zero, chip.sizeDelta, 22, TextAlignmentOptions.Center);
            ArchiveUi.ApplyFont(eraText, ArchiveUi.FontRole.Technical);
            eraText.color = PastColor;
            goalText = ArchiveUi.Text(panel, "", new Vector2(186, -12), new Vector2(716, 42), 26);
            feedbackText = ArchiveUi.Text(panel, "", new Vector2(18, -60), new Vector2(884, 34), 22);
            feedbackText.name = "Puzzle feedback";
            feedbackText.color = ArchiveUi.Cyan;
            feedbackText.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (input == null) input = FindFirstObjectByType<PlayerInputReader>();
            bool visible = area != null && input != null && !input.IsGameplayBlockedByInterface
                && Time.timeScale > 0f && !area.Completed && area.ContainsPosition(input.transform.position);
            group.alpha = visible ? 1f : 0f;
            if (!visible) return;

            eraText.text = area.Era.ToString().ToUpperInvariant();
            eraText.color = area.Era switch
            {
                TimelineEra.Past => PastColor,
                TimelineEra.Present => PresentColor,
                _ => FutureColor
            };
            goalText.text = area.IsShifting ? "Scanning timeline..."
                : Goal(input.transform.position, input.GetComponent<CharacterController>());
            bool flashing = flash != null && Time.unscaledTime < flashUntil;
            panel.sizeDelta = new Vector2(920, flashing ? 104 : 68);
            feedbackText.gameObject.SetActive(flashing);
            if (flashing) feedbackText.text = flash;
        }

        public string Goal(Vector3 position, CharacterController player)
        {
            if (area.IsSolved && area.puzzleKind == TimelinePuzzleKind.LibraryStaircase)
            {
                if (area.IsOnFinishLanding(position, player))
                    return area.Era == TimelineEra.Present ? "Approach the record and press E to recover it"
                        : "Shift to the Present here to recover the record";
                return area.Era == TimelineEra.Future ? "Cross the repaired bridge to the final platform"
                    : "Shift to the Future, then follow the upper route to the final platform";
            }
            if (area.IsSolved)
                return area.Era == TimelineEra.Present ? "Recover the record at the glowing platform"
                    : "Shift back to the Present to finish";
            if (area.puzzleKind == TimelinePuzzleKind.MainIntersectionBridge)
            {
                if (!area.WestAligned) return InPast("turn the support at the control");
                if (!area.ReachedIsland)
                    return area.Era == TimelineEra.Future ? "Cross the bridge to the island"
                        : "Shift to the Future and cross the bridge to the island";
                if (!area.FirstChanged) return InPast("push the block onto its socket");
                return InPast("turn the support east at the control");
            }
            if (!area.FirstChanged) return InPast("push the block onto the glowing socket");
            return InPast("push the brace onto its socket");
        }

        private string InPast(string goal) => area.Era == TimelineEra.Past
            ? char.ToUpperInvariant(goal[0]) + goal.Substring(1)
            : "Shift to the Past, then " + goal;
    }
}
