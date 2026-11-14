using Caffeine.Gameplay.Player;
using Caffeine.Gameplay.UI;
using Caffeine.Gameplay.Maze;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Caffeine.Gameplay.Puzzles
{
    public sealed class PuzzleEntrance : MonoBehaviour
    {
        public int puzzleIndex;
        public string locationName;
        public string puzzleName;
        public float interactionRadius = 3f;
        public string enterPrompt = "[E] Enter archive puzzle";
        public string lockedPrompt = "Accept this round at the merchant first";
        public TextMeshPro marker;
        public Transform interactionPoint;
        public Transform InteractionTarget => interactionPoint != null ? interactionPoint : transform;

        public bool CanInteractFrom(Vector3 position, Transform actor = null)
        {
            if (Vector3.Distance(position, InteractionTarget.position) > interactionRadius)
            {
                return false;
            }
            // keep the existing interaction volume for entrances without an interaction point, including the Boss door
            if (interactionPoint == null)
            {
                return true;
            }

            if (Vector3.Dot(position - transform.position, transform.forward) < .05f)
            {
                return false;
            }

            Vector3 start = position + Vector3.up * .9f;
            Vector3 delta = InteractionTarget.position + Vector3.up * .9f - start;
            foreach (var hit in Physics.RaycastAll(start, delta.normalized, delta.magnitude,
                ~0, QueryTriggerInteraction.Ignore))
            {
                if (actor == null || !hit.collider.transform.IsChildOf(actor))
                {
                    return false;
                }
            }

            return true;
        }

        private PlayerInputReader player;
        private TutorialPresenter screens;
        private MazeSystem maze;
        private GameObject promptCanvas;
        private RectTransform promptRoot;
        private TMP_Text promptText;

        private void Start()
        {
            player = FindFirstObjectByType<PlayerInputReader>();
            screens = FindFirstObjectByType<TutorialPresenter>();
            maze = FindFirstObjectByType<MazeSystem>();
            promptCanvas = new GameObject("Archive entrance prompt", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = promptCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 35;
            var scaler = promptCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            promptRoot = ArchiveUi.Prompt(canvas.transform, "E", new Vector2(0, 48), new Vector2(720, 112), out promptText);
            promptRoot.gameObject.SetActive(false);
        }

        private bool CanInteract => player != null && !player.IsGameplayBlocked
            && (screens == null || !screens.IsShowing)
            && (maze == null || !maze.IsSessionOpen)
            && Time.timeScale > 0f
            && CanInteractFrom(player.transform.position, player.transform);

        private void Update()
        {
            if (CanInteract && player.TryConsumeInteract())
                TryEnter();
        }

        public bool TryEnter()
        {
            if (!CanInteract || !PuzzleRunState.CanEnter(puzzleIndex) || PuzzleRunState.HasPendingArrival)
                return false;
            PuzzleRunState.Enter(puzzleIndex, player);
            return true;
        }

        private void LateUpdate()
        {
            if (marker != null && Camera.main != null)
                marker.transform.rotation = Quaternion.LookRotation(marker.transform.position - Camera.main.transform.position);
            RefreshPrompt();
        }

        private void RefreshPrompt()
        {
            if (promptRoot == null) return;
            promptRoot.gameObject.SetActive(CanInteract);
            if (!CanInteract) return;
            string action = PuzzleRunState.CanEnter(puzzleIndex) ? enterPrompt : lockedPrompt;
            float height = 112;
            if (puzzleIndex < PuzzleRunState.BossIndex && PuzzleRunState.IsSolved(puzzleIndex))
                action = PuzzleRunState.IsUnlocked(puzzleIndex) ? "Record delivered · task complete"
                    : "Record recovered — collect your weapon from the merchant";
            else if (puzzleIndex < PuzzleRunState.BossIndex && puzzleIndex > 1
                && !PuzzleRunState.IsUnlocked(puzzleIndex - 1))
                action = "Collect the previous weapon at the merchant first";
            else if (puzzleIndex == PuzzleRunState.BossIndex && !PuzzleRunState.AllPuzzlesSolved)
            {
                string missing = PuzzleRunState.UnsolvedPuzzles();
                action = "Recover the missing reference records:\n" + missing;
                height += 26 * (missing.Split('\n').Length + 1);
            }
            else if (puzzleIndex == PuzzleRunState.BossIndex && !PuzzleRunState.BossUnlocked)
                action = "Return to the merchant to verify the records and authorise access";
            promptRoot.sizeDelta = new Vector2(720, height);
            promptText.rectTransform.sizeDelta = new Vector2(616, height);
            promptText.text = locationName + " · " + puzzleName + "\n" + action.Replace("[E] ", "");
        }

        private void OnDisable() { if (promptRoot != null) promptRoot.gameObject.SetActive(false); }
        private void OnDestroy() { if (promptCanvas != null) Destroy(promptCanvas); }
    }
}
