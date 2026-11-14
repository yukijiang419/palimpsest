using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Maze;
using UnityEngine;
using UnityEngine.UI;

namespace Caffeine.Gameplay.UI
{
    [DisallowMultipleComponent]
    // display one board cell and its effect token
    public sealed class MazeCellView : MonoBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private PolygonTokenGraphic token;
        private CellKind kind;
        private OutcomeType outcome;
        private bool hasOutcome;
        private GameplayConfig.UiSettings ui;
        private void Awake()
        {
            background.raycastTarget = false;
            token.raycastTarget = false;
        }

        public void Configure(CellKind cellKind, GameplayConfig.MazeSettings mazeSettings, GameplayConfig.UiSettings uiSettings)
        {
            kind = cellKind;
            ui = uiSettings;
            hasOutcome = MazeRules.TryGetOutcome(kind, out outcome);
            background.sprite = null;
            background.type = Image.Type.Simple;
            // Cells are reused across sessions; clear the previous board's marker first.
            foreach (Transform child in transform)
            {
                if (child.name == "Cell marker")
                {
                    child.gameObject.SetActive(false);
                }
            }

            if (kind == CellKind.Start || kind == CellKind.Obstacle)
            {
                var rect = transform.Find("Cell marker") as RectTransform;
                if (rect == null)
                {
                    rect = ArchiveUi.Stretch("Cell marker", transform);
                }

                float inset = kind == CellKind.Obstacle ? .23f : .16f;
                rect.anchorMin = Vector2.one * inset;
                rect.anchorMax = Vector2.one * (1 - inset);
                rect.gameObject.SetActive(true);
                var marker = ArchiveUi.Surface(rect, kind == CellKind.Start ? "node-start" : "node-block");
                marker.preserveAspect = true;
                marker.color = kind == CellKind.Obstacle ? new Color(.58f, .63f, .62f, .78f) : Color.white;
            }

            // only effect cells need a polygon token
            token.gameObject.SetActive(hasOutcome);
            if (hasOutcome)
            {
                token.ConfigureStyle(MazeRules.GetSideCount(outcome), mazeSettings.StageOneThreshold, mazeSettings.StageTwoThreshold, GetOutcomeColor(), ui.NeutralColor, ui.TokenOutlineThickness);
            }
        }

        public void Render(bool visited)
        {
            // The route graphic identifies the moving cursor.
            if (kind == CellKind.Start)
            {
                background.color = ui.CellStartColor;
            }
            else if (visited)
            {
                background.color = ui.CellVisitedColor;
            }
            else if (kind == CellKind.Obstacle)
            {
                background.color = ui.CellObstacleColor;
            }
            else
            {
                background.color = ui.CellEmptyColor;
            }

            if (hasOutcome)
            {
                token.SetCollected(visited);
            }
        }

        private Color GetOutcomeColor()
        {
            if (outcome == OutcomeType.Recovery)
            {
                return ui.RecoveryColor;
            }

            return outcome == OutcomeType.Damage ? ui.DamageColor : ui.ControlColor;
        }
    }
}
