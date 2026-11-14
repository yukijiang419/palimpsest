using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Maze;
using Caffeine.Gameplay.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Caffeine.Gameplay.UI
{
    [DisallowMultipleComponent]
    // connect the maze session to the board UI and mouse gestures
    public sealed class MazeBoardPresenter : MonoBehaviour
    {
        [SerializeField] private MazeSystem mazeSystem;
        [SerializeField] private MazeGestureInterpreter gestureInterpreter;
        [SerializeField] private RectTransform boardRoot;
        [SerializeField] private MazeCellView cellPrefab;
        [SerializeField] private RectTransform legendRoot;
        [SerializeField] private TMP_Text recoveryLegend;
        [SerializeField] private TMP_Text damageLegend;
        [SerializeField] private TMP_Text controlLegend;

        private GameplayConfig.MazeSettings mazeSettings;
        private GameplayConfig.UiSettings uiSettings;
        private MazeCellView[] cells;
        private MazeBoardSession session;
        private MazePathGraphic pathGraphic;
        private PolygonTokenGraphic[] legendIcons;
        private MazeOutcomeParticleLayer particles;
        private GameTimeController timeController;

        private void Awake()
        {
            boardRoot.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            mazeSystem.SessionChanged += HandleSessionChanged;
            mazeSystem.BoardChanged += RenderSession;
            mazeSystem.OutcomeResolved += PresentResolution;
            gestureInterpreter.DirectionRequested += HandleDirectionRequested;
        }

        private void Start()
        {
            mazeSettings = ConfigSystem.Instance.Config.Maze;
            uiSettings = ConfigSystem.Instance.Config.UI;
            timeController = FindFirstObjectByType<GameTimeController>();
            ApplyLayout();
            HandleSessionChanged();
        }

        private void OnDisable()
        {
            mazeSystem.SessionChanged -= HandleSessionChanged;
            mazeSystem.BoardChanged -= RenderSession;
            mazeSystem.OutcomeResolved -= PresentResolution;
            gestureInterpreter.DirectionRequested -= HandleDirectionRequested;
            gestureInterpreter.SetActive(false);
            boardRoot.gameObject.SetActive(false);
        }

        // show the board and enable gestures only while a session is open
        private void HandleSessionChanged()
        {
            session = mazeSystem.CurrentSession;
            bool visible = session != null;
            if (isActiveAndEnabled && mazeSystem.isActiveAndEnabled && boardRoot.gameObject.activeSelf != visible)
            {
                FeedbackAudioController.Play(visible ? FeedbackSound.Open : FeedbackSound.Close);
            }
            boardRoot.gameObject.SetActive(visible);
            gestureInterpreter.SetActive(visible);

            if (visible && mazeSettings != null)
            {
                ConfigureBoard(session.Board);
                RenderSession();
            }
        }

        private void HandleDirectionRequested(MoveDirection direction)
        {
            mazeSystem.RequestMove(direction);
        }

        // reuse the cell views and apply the new board layout
        private void ConfigureBoard(MazeBoard board)
        {
            EnsureCells(board);
            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    GridPosition position = new(x, y);
                    cells[y * board.Width + x].Configure(
                        board.GetCell(position),
                        mazeSettings,
                        uiSettings);
                }
            }
        }

        private void EnsureCells(MazeBoard board)
        {
            if (cells != null)
            {
                return;
            }

            cells = new MazeCellView[board.Width * board.Height];
            // create the top row first to match the UI grid order
            for (int y = board.Height - 1; y >= 0; y--)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    var cell = Instantiate(cellPrefab, boardRoot);
                    cells[y * board.Width + x] = cell;
                    float step = uiSettings.BoardCellSize + uiSettings.BoardSpacing;
                    ArchiveUi.Place((RectTransform)cell.transform, new Vector2(0, 1), new Vector2(0, 1),
                        new Vector2(uiSettings.BoardPadding + x * step, -uiSettings.BoardPadding - (board.Height - 1 - y) * step),
                        Vector2.one * uiSettings.BoardCellSize);
                }
            }
        }

        // update the path highlights, cursor, and outcome progress
        private void RenderSession()
        {
            if (session == null || cells == null) return;
            MazeBoard board = session.Board;
            OutcomeProgress progress = session.Progress;
            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    GridPosition position = new(x, y);
                    cells[y * board.Width + x].Render(
                        session.HasVisited(position));
                }
            }
            pathGraphic.transform.SetAsLastSibling();
            pathGraphic.SetPath(session, uiSettings.BoardCellSize, uiSettings.BoardSpacing, uiSettings.BoardPadding);
            for (int i = 0; i < legendIcons.Length; i++)
            {
                legendIcons[i].SetProgress(progress.GetCount((OutcomeType)i));
            }
        }

        private void Update()
        {
            if (legendIcons == null)
            {
                return;
            }

            float delta = timeController != null && timeController.IsPaused ? 0f : Time.unscaledDeltaTime;
            foreach (var icon in legendIcons)
            {
                icon.AnimateCharge(delta);
            }
        }

        private void PresentResolution(MazeResolution result)
        {
            if (particles == null || legendIcons == null)
            {
                return;
            }

            particles.Release(result, legendIcons);
        }

        private void ApplyLayout()
        {
            float gridSize = uiSettings.BoardPadding * 2 + uiSettings.BoardCellSize * mazeSettings.BoardSize
                + uiSettings.BoardSpacing * (mazeSettings.BoardSize - 1);
            ArchiveUi.Place(boardRoot, new Vector2(1, 1), new Vector2(1, 1), uiSettings.BoardOffset,
                new Vector2(gridSize, gridSize + 78));
            boardRoot.GetComponent<GridLayoutGroup>().enabled = false;
            ArchiveUi.Frame(boardRoot, ArchiveUi.Copper);
            // Replace the prototype's side legend with three shape-and-label pairs below the grid.
            legendRoot.gameObject.SetActive(false);
            var legend = ArchiveUi.Rect("Path effects", boardRoot, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(0, -gridSize), new Vector2(gridSize, 78));
            string[] names = { "Heal", "Break", "Bind" };
            Color[] colors = { uiSettings.RecoveryColor, uiSettings.DamageColor, uiSettings.ControlColor };
            legendIcons = new PolygonTokenGraphic[3];
            for (int i = 0; i < 3; i++)
            {
                float columnWidth = (gridSize - 24) / 3;
                float x = 12 + i * columnWidth;
                var icon = ArchiveUi.Rect(names[i] + " icon", legend, new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(x + (columnWidth - 36) * 0.5f, -4), new Vector2(36, 36));
                legendIcons[i] = icon.gameObject.AddComponent<PolygonTokenGraphic>();
                legendIcons[i].ConfigureStyle(i + 3, mazeSettings.StageOneThreshold, mazeSettings.StageTwoThreshold,
                    colors[i], uiSettings.NeutralColor, 2);
                legendIcons[i].SetProgress(0);
                ArchiveUi.Text(legend, names[i], new Vector2(x, -38), new Vector2(columnWidth, 30), 22,
                    TextAlignmentOptions.Center);
            }
            var pathRect = ArchiveUi.Stretch("Continuous path", boardRoot);
            pathGraphic = pathRect.gameObject.AddComponent<MazePathGraphic>();
            pathGraphic.raycastTarget = false;
            particles = MazeOutcomeParticleLayer.Create(GetComponentInParent<Canvas>(), timeController, uiSettings);

        }
    }
}
