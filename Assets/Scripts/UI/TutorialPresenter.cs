using Caffeine.Gameplay.Persistence;
using System.Collections;
using Caffeine.Gameplay.CameraSystem;
using Caffeine.Gameplay.Combat;
using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Maze;
using Caffeine.Gameplay.Player;
using Caffeine.Gameplay.Puzzles;
using Caffeine.Gameplay.Presentation;
using Caffeine.Gameplay.Rendering;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Caffeine.Gameplay.UI
{
    // Retains the existing scene/prefab script identity and wired gameplay references.
    [DefaultExecutionOrder(-300)]
    [DisallowMultipleComponent]
    public sealed class TutorialPresenter : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader inputReader;
        [SerializeField] private GameTimeController timeController;
        [SerializeField] private ThirdPersonCameraController cameraController;

        private enum Screen { Playing, Title, Pause, Settings, GameOver, Victory, Opening, Tutorial, ArchiveRecord, LevelSelect, Map }
        private Screen screen;
        private Screen settingsReturn;
        private RectTransform overlay;
        private RectTransform panel;
        private CanvasGroup gameplayVisibility;
        private HealthShield health;
        private PlayerDeathController deathController;
        private MazeSystem maze;
        private MinimapPresenter minimap;
        private Button firstButton;
        private Image titleBackground;
        private bool loading;
        private bool hasShownScreen;
        private TMP_Text levelSelectError;
        private TMP_Text pauseError;
        private Image dimmer;
        private Image openingFade;
        private TMP_Text openingCaption;
        private TypewriterText storyTypewriter;
        private int openingCaptionShot = -1;
        private GameObject openingLocation;
        private OpeningSequenceController opening;
        private int tutorialPage;
        private bool tutorialReplay;
        private bool combatTutorial;
        private string archiveRecord;
        private string archiveTitle;
        private System.Action archiveContinue;
        private bool puzzleScene;
        private GameplayConfig Config => ConfigSystem.Instance != null ? ConfigSystem.Instance.Config : null;
        // Puzzle scenes explain timeline shifting instead of the campus controls.
        private TutorialGuide Guide => Config == null ? null
            : combatTutorial ? Config.Story.combatTutorialGuide
            : puzzleScene && Config.Story.puzzleTutorialGuide != null ? Config.Story.puzzleTutorialGuide : Config.Story.tutorialGuide;
        private TutorialGuide.Page[] TutorialPages => tutorialReplay && !puzzleScene && Config.Story.combatTutorialGuide != null
            ? Guide.pages.Concat(Config.Story.combatTutorialGuide.pages).ToArray() : Guide?.pages;
        public string CurrentScreen => screen.ToString();
        public bool IsShowing => screen != Screen.Playing;
        public bool IsMapShowing => screen == Screen.Map;
        public bool IsGameplayActive => screen == Screen.Playing || screen == Screen.Pause || screen == Screen.LevelSelect || screen == Screen.Map
            || screen == Screen.Settings && settingsReturn == Screen.Pause
            || screen == Screen.Tutorial && tutorialReplay || screen == Screen.ArchiveRecord;

        private void Awake()
        {
            health = inputReader.GetComponent<HealthShield>();
            deathController = inputReader.GetComponent<PlayerDeathController>();
            maze = FindFirstObjectByType<MazeSystem>();
            minimap = FindFirstObjectByType<MinimapPresenter>();
            var canvas = GetComponentInParent<Canvas>();
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            gameplayVisibility = canvas.GetComponent<CanvasGroup>();
            if (gameplayVisibility == null) gameplayVisibility = canvas.gameObject.AddComponent<CanvasGroup>();
            overlay = ArchiveUi.Stretch("Archive screens", transform);
            var menuCanvas = overlay.gameObject.AddComponent<Canvas>();
            menuCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            menuCanvas.overrideSorting = true;
            menuCanvas.sortingOrder = 100;
            var menuScaler = overlay.gameObject.AddComponent<CanvasScaler>();
            menuScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            menuScaler.referenceResolution = new Vector2(1920, 1080);
            menuScaler.matchWidthOrHeight = 0.5f;
            overlay.gameObject.AddComponent<GraphicRaycaster>();
            // Ignore the gameplay canvas' visibility when this object is nested under it.
            overlay.gameObject.AddComponent<CanvasGroup>().ignoreParentGroups = true;
            titleBackground = ArchiveUi.Surface(ArchiveUi.Stretch("Title campus background", overlay), "title-background");
            var backgroundAspect = titleBackground.gameObject.AddComponent<AspectRatioFitter>();
            backgroundAspect.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            backgroundAspect.aspectRatio = titleBackground.sprite != null
                ? titleBackground.sprite.rect.width / titleBackground.sprite.rect.height : 16f / 9f;
            dimmer = ArchiveUi.Paint(ArchiveUi.Stretch("Dimmer", overlay), new Color(0.015f, 0.02f, 0.02f, 0.64f), true);
            AudioListener.volume = PlayerPrefs.GetFloat("Archive.MasterVolume", 1f);
            bool arriving = PuzzleRunState.HasPendingArrival;
            puzzleScene = FindFirstObjectByType<TimelinePuzzleArea>() != null;
            if (arriving)
            {
                PuzzleRunState.PlaceReturningPlayer(inputReader);
                inputReader.gameObject.AddComponent<PuzzleRunRestorer>();
            }
            if (arriving && puzzleScene && PuzzleRunState.TryShowPuzzleTutorial()) BeginTutorial(false);
            else Show(arriving ? Screen.Playing : Screen.Title);
        }

        private void OnEnable() => health.Died += OnPlayerDied;

        private void OnDisable()
        {
            if (storyTypewriter != null) storyTypewriter.Cancel();
            if (minimap != null) minimap.CloseFullMap();
            if (health != null) health.Died -= OnPlayerDied;
            if (opening != null) Destroy(opening.gameObject);
            if (inputReader != null) inputReader.SetTutorialInputBlocked(false);
            if (timeController != null) timeController.SetTutorialPaused(false);
            if (cameraController != null) cameraController.SetTutorialCursorReleased(false);
        }

        private UnityEngine.UI.Button newGameButton;
        private UnityEngine.UI.Button continueButton;
        private void Update()
        {
            var saves = SaveGameService.Instance;
            if (saveLabel != null) saveLabel.text = VisibleSaveError(saves != null ? saves.Status : null);
            if (screen == Screen.Title && saves != null)
            {
                if (newGameButton != null) newGameButton.interactable = saves.Ready && !saves.Busy;
                if (continueButton != null) continueButton.interactable = saves.Ready && saves.HasSave && !saves.Busy;
            }
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            HandleMenuInput(keyboard.mKey.wasPressedThisFrame, keyboard.escapeKey.wasPressedThisFrame);
        }

        private void HandleMenuInput(bool mapPressed, bool escapePressed)
        {
            if (loading) return;
            if (screen == Screen.Map)
            {
                if (mapPressed || escapePressed) Show(Screen.Playing);
                return;
            }
            if (!escapePressed)
            {
                if (mapPressed && screen == Screen.Playing && !puzzleScene
                    && SceneManager.GetActiveScene().name == "StartScene"
                    && !timeController.BlocksPauseMenu && !inputReader.IsGameplayBlocked
                    && health != null && health.IsInitialized && !health.IsDead
                    && minimap != null && minimap.CanShowFullMap)
                    Show(Screen.Map);
                return;
            }
            if (screen == Screen.Playing && maze != null
                && (maze.IsSessionOpen || maze.LastCancellationFrame == Time.frameCount))
            {
                maze.CancelSession(Caffeine.Gameplay.Maze.MazeEndReason.Cancelled);
                return;
            }
            if (screen == Screen.Settings) { PlayerPrefs.Save(); Show(settingsReturn); }
            else if (screen == Screen.LevelSelect) Show(Screen.Pause);
            else if (screen == Screen.Opening) SkipIntro();
            else if (screen == Screen.Tutorial) FinishTutorial();
            else if (screen == Screen.Pause) Show(Screen.Playing);
            else if (screen == Screen.Playing && !timeController.BlocksPauseMenu)
            {
                Show(Screen.Pause);
            }
        }

        private void Show(Screen next)
        {
            if(next == Screen.Pause) SaveGameService.RequestSave();
            if (storyTypewriter != null) storyTypewriter.Cancel();
            storyTypewriter = null;
            if (hasShownScreen && screen != next)
            {
                FeedbackAudioController.Play(next == Screen.Playing ? FeedbackSound.Close : FeedbackSound.Open);
            }
            hasShownScreen = true;
            if (screen == Screen.Map && minimap != null) minimap.CloseFullMap();
            screen = next;
            bool blocked = IsShowing;
            if (blocked && maze != null) maze.CancelSession();
            inputReader.SetTutorialInputBlocked(blocked);
            timeController.SetTutorialPaused(blocked);
            cameraController.SetTutorialCursorReleased(blocked);
            overlay.gameObject.SetActive(blocked);
            bool showCampusBackground = next == Screen.Title || next == Screen.Settings;
            titleBackground.gameObject.SetActive(showCampusBackground);
            dimmer.color = new Color(0.015f, 0.02f, 0.02f, next == Screen.Opening ? 0f : showCampusBackground ? .2f : .64f);
            bool showHud = next == Screen.Playing || next == Screen.Pause || next == Screen.LevelSelect ||
                (next == Screen.Settings && settingsReturn == Screen.Pause);
            gameplayVisibility.alpha = showHud ? 1 : 0;
            gameplayVisibility.blocksRaycasts = !blocked;
            saveLabel = null;
            newGameButton = continueButton = null;
            if (panel != null) { panel.gameObject.SetActive(false); Destroy(panel.gameObject); }
            if (!blocked) return;
            if (next == Screen.Opening) { BuildOpening(); return; }
            if (next == Screen.Tutorial) { BuildTutorial(); return; }
            if (next == Screen.ArchiveRecord) { BuildArchiveRecord(); return; }
            if (next == Screen.Map) { BuildCampusMap(); return; }
            Color accent = next == Screen.GameOver ? ArchiveUi.Red : next == Screen.Victory ? ArchiveUi.Cyan : ArchiveUi.Copper;
            float panelWidth = next == Screen.LevelSelect ? 640 : 540;
            float panelHeight = next == Screen.LevelSelect ? 620 : next == Screen.Pause ? 660 : next == Screen.Title ? 440 : 500;
            panel = ArchiveUi.Rect(next.ToString(), overlay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(panelWidth, panelHeight));
            ArchiveUi.Frame(panel, accent);
            string title = next switch {
                Screen.Title => "Palimpsest", Screen.Pause => "Paused", Screen.GameOver => "Game Over",
                Screen.Victory => "Victory", Screen.LevelSelect => "Skip to Level", _ => "Settings"
            };
            var heading = ArchiveUi.Text(panel, title, new Vector2(24, -42), new Vector2(panelWidth - 48, 76), 48, TextAlignmentOptions.Center);
            ArchiveUi.ApplyFont(heading, ArchiveUi.FontRole.Heading);
            heading.color = next == Screen.GameOver || next == Screen.Victory ? accent : ArchiveUi.Paper;
            var rule = ArchiveUi.Rect("Heading rule", panel, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -138), new Vector2(380, 1));
            ArchiveUi.Paint(rule, accent);
            firstButton = null;
            switch (next)
            {
                case Screen.Title:
                    var saves = SaveGameService.Instance;
                    newGameButton = AddButton("New Game", -174, ConfirmNewGame, accent);
                    newGameButton.interactable = saves.Ready && !saves.Busy;
                    continueButton = AddButton("Continue", -246, () => saves.Continue(), accent);
                    continueButton.interactable = saves.Ready && saves.HasSave && !saves.Busy;
                    AddButton("Settings", -318, OpenSettings, accent);
                    break;
                case Screen.Pause:
                    AddButton("Resume", -168, () => Show(Screen.Playing), accent);
                    AddButton("Skip to Level", -240, () => Show(Screen.LevelSelect), accent);
                    var skip = AddButton("Skip Current Level", -312, SkipCurrentLevel, accent);
                    skip.interactable = PuzzleRunState.TryGetSkippablePuzzle(out _);
                    AddButton("How to Play", -384, ReplayTutorial, accent);
                    AddButton("Settings", -456, OpenSettings, accent);
                    AddButton("Return to Title", -528, ReturnToTitle, accent);
                    pauseError = WrappedText(panel, "", new Vector2(30, -590), new Vector2(480, 48), 20);
                    pauseError.color = ArchiveUi.Red;
                    pauseError.alignment = TextAlignmentOptions.Center;
                    break;
                case Screen.LevelSelect:
                    BuildLevelSelect(accent);
                    break;
                case Screen.GameOver:
                    Caption("The current timeline has collapsed.", -198);
                    AddButton(deathController.RespawnButtonLabel, -280, Respawn, accent);
                    AddButton("Return to Title", -360, ReturnToTitle, accent);
                    break;
                case Screen.Victory:
                    var ending = WrappedText(panel, "", new Vector2(42, -172), new Vector2(456, 150), 24);
                    PlayStoryText(ending, Config != null && Config.Story != null ? Config.Story.ending :
                        "The reconstruction cycle has stopped. Recovery can begin.");
                    AddButton("Return to Title", -366, ContinueVictory, accent);
                    break;
                case Screen.Settings:
                    BuildSettings(accent);
                    break;
            }
            if (next != Screen.Settings)
            {
                saveLabel = WrappedText(panel, VisibleSaveError(SaveGameService.Instance != null ? SaveGameService.Instance.Status : null),
                    new Vector2(24, -panelHeight + 50), new Vector2(panelWidth - 48, 45), 18);
                saveLabel.gameObject.name = "Save error";
                saveLabel.color = ArchiveUi.Red;
                saveLabel.alignment = TextAlignmentOptions.Center;
            }
            if (EventSystem.current != null && firstButton != null)
                EventSystem.current.SetSelectedGameObject(firstButton.gameObject);
        }

        private void BuildCampusMap()
        {
            panel = ArchiveUi.Rect("Campus map", overlay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1540f, 930f));
            ArchiveUi.Frame(panel, ArchiveUi.Copper);
            ArchiveUi.Text(panel, "Campus Map", new Vector2(48f, -28f), new Vector2(1100f, 64f), 42f,
                fontRole: ArchiveUi.FontRole.Heading);
            ArchiveUi.Text(panel, "N ↑  ·  North up", new Vector2(1200f, -38f), new Vector2(292f, 44f), 24f,
                TextAlignmentOptions.MidlineRight, ArchiveUi.FontRole.Technical);
            minimap.ShowFullMap(panel);
            minimap.BuildMapLegend(panel);
            var close = ArchiveUi.Button(panel, "Close · M / Esc", -854f, () => Show(Screen.Playing), ArchiveUi.Copper);
            close.GetComponent<RectTransform>().anchoredPosition = new Vector2(572f, -854f);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(close.gameObject);
        }

        private void Caption(string text, float y) => ArchiveUi.Text(panel, text,
            new Vector2(24, y), new Vector2(492, 36), 20, TextAlignmentOptions.Center);

        private Button AddButton(string text, float y, UnityEngine.Events.UnityAction action, Color accent, float width = 300)
        {
            var button = ArchiveUi.Button(panel, text, y, action, accent);
            button.GetComponent<RectTransform>().sizeDelta = new Vector2(width, 54);
            button.GetComponentInChildren<TMP_Text>().rectTransform.sizeDelta = new Vector2(width, 54);
            if (firstButton == null) firstButton = button;
            return button;
        }

        private void BuildLevelSelect(Color accent)
        {
            var definitions = Config != null ? Config.Quests.Definitions
                : System.Array.Empty<Caffeine.Gameplay.Quests.MerchantQuestDefinition>();
            int row = 0;
            foreach (var definition in definitions.Where(d => d != null && d.HasPuzzle)
                .OrderBy(d => d.Puzzle.puzzleIndex))
            {
                int target = definition.Puzzle.puzzleIndex;
                AddButton(definition.Puzzle.displayName, -168 - row * 72, () => JumpToLevel(target), accent, 540);
                row++;
            }
            string bossLabel = Config != null ? Config.Quests.BossSceneName : "Boss Arena";
            if (bossLabel == "BossArena") bossLabel = "Boss Arena";
            AddButton(bossLabel, -384, () => JumpToLevel(PuzzleRunState.BossIndex), accent, 540);
            AddButton("Back", -480, () => Show(Screen.Pause), accent);
            levelSelectError = WrappedText(panel, "", new Vector2(36, -556), new Vector2(568, 48), 20);
            levelSelectError.color = ArchiveUi.Red;
            levelSelectError.alignment = TextAlignmentOptions.Center;
        }

        private void JumpToLevel(int index)
        {
            if (loading || screen != Screen.LevelSelect) return;
            loading = true;
            if (PuzzleRunState.TryJumpTo(index, inputReader)) return;
            loading = false;
            levelSelectError.text = "Unable to load this level.";
        }

        private void SkipCurrentLevel()
        {
            if (loading || screen != Screen.Pause) return;
            bool onCampus = SceneManager.GetActiveScene().name == "StartScene";
            loading = true;
            if (PuzzleRunState.TrySkipCurrent(inputReader))
            {
                if (onCampus)
                {
                    loading = false;
                    Show(Screen.Playing);
                }
                return;
            }
            loading = false;
            pauseError.text = "Unable to skip this level or return to the merchant.";
        }

        private void OpenSettings() { settingsReturn = screen; Show(Screen.Settings); }

        private void BuildSettings(Color accent)
        {
            var value = ArchiveUi.Text(panel, $"Master volume  {Mathf.RoundToInt(AudioListener.volume * 100)}%",
                new Vector2(70, -170), new Vector2(400, 32));
            var rect = ArchiveUi.Rect("Master volume", panel, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(70, -214), new Vector2(400, 28));
            ArchiveUi.Surface(rect, "stamina-track", true);
            var fillArea = ArchiveUi.Stretch("Fill area", rect);
            fillArea.offsetMin = new Vector2(8, 8); fillArea.offsetMax = new Vector2(-8, -8);
            var fill = ArchiveUi.Paint(ArchiveUi.Stretch("Volume", fillArea), accent);
            var handleArea = ArchiveUi.Stretch("Handle area", rect);
            handleArea.offsetMin = new Vector2(8, 0); handleArea.offsetMax = new Vector2(-8, 0);
            var handle = ArchiveUi.Surface(ArchiveUi.Rect("Handle", handleArea, Vector2.zero,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(28, 32)), "keycap", true);
            var slider = rect.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.minValue = 0; slider.maxValue = 1; slider.value = AudioListener.volume;
            slider.onValueChanged.AddListener(volume => {
                AudioListener.volume = volume;
                PlayerPrefs.SetFloat("Archive.MasterVolume", volume);
                value.text = $"Master volume  {Mathf.RoundToInt(volume * 100)}%";
            });
            ArchiveUi.Text(panel, "Graphics Quality", new Vector2(70, -270), new Vector2(400, 32));
            var qualityLabel = ArchiveUi.Text(panel, GraphicsQualitySettings.Current.ToString(),
                new Vector2(134, -310), new Vector2(272, 54), 24, TextAlignmentOptions.Center);
            qualityLabel.gameObject.name = "Quality value";
            void ChangeQuality(int direction)
            {
                int index = ((int)GraphicsQualitySettings.Current + direction + 3) % 3;
                GraphicsQualitySettings.SetQuality((GraphicsQuality)index);
                qualityLabel.text = GraphicsQualitySettings.Current.ToString();
            }
            var previous = QualityButton("Previous quality", "<", 70, () => ChangeQuality(-1), accent);
            var next = QualityButton("Next quality", ">", 414, () => ChangeQuality(1), accent);
            var back = AddButton("Back", -396, () => { PlayerPrefs.Save(); Show(settingsReturn); }, accent);
            slider.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = back, selectOnDown = previous };
            previous.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = slider,
                selectOnDown = back, selectOnLeft = next, selectOnRight = next };
            next.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = slider,
                selectOnDown = back, selectOnLeft = previous, selectOnRight = previous };
            back.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = previous, selectOnDown = slider };
            firstButton = previous;
        }

        private UnityEngine.UI.Button QualityButton(string name, string text, float x, UnityEngine.Events.UnityAction action, Color accent)
        {
            var button = ArchiveUi.Button(panel, text, -310, action, accent);
            button.gameObject.name = name;
            ArchiveUi.Place((RectTransform)button.transform, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(x, -310), new Vector2(56, 54));
            button.GetComponentInChildren<TMP_Text>().rectTransform.sizeDelta = new Vector2(56, 54);
            return button;
        }

        internal static string VisibleSaveError(string status) => status switch
        {
            null or "" or "Reading save..." or "Continue available" or "Saving..." or "Saved" or "Recovered previous save." => "",
            _ => status
        };

        private static TMP_Text WrappedText(Transform parent, string text, Vector2 position, Vector2 size, float fontSize)
        {
            var label = ArchiveUi.Text(parent, text, position, size, fontSize, TextAlignmentOptions.TopLeft);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }

        public void StartRun()
        {
            if (screen != Screen.Title || loading) return;
            if (SceneManager.GetActiveScene().name != "StartScene") { Show(Screen.Playing); return; }
            Show(Screen.Opening);
            // Resolve spawn overlap while the opening screen hides movement.
            var body = inputReader.GetComponent<CharacterController>();
            if (body != null && body.enabled)
                body.Move(Vector3.down * Mathf.Max(0.001f, body.minMoveDistance + 0.001f));
            if (Config == null || Config.Story.openingSequence == null)
            {
                Debug.LogError("GameplayConfig opening sequence is missing. Continuing to the tutorial.", this);
                BeginTutorial(false);
                return;
            }
            opening = Instantiate(Config.Story.openingSequence);
            opening.Completed += OpeningCompleted;
            opening.PresentationChanged += UpdateOpening;
            opening.Play();
        }

        private void BuildOpening()
        {
            openingCaptionShot = -1;
            panel = ArchiveUi.Stretch("Opening", overlay);
            var top = ArchiveUi.Rect("Letterbox top", panel, new Vector2(0, 1), new Vector2(0, 1),
                Vector2.zero, new Vector2(1920, 64));
            ArchiveUi.Paint(top, Color.black);
            var bottom = ArchiveUi.Rect("Subtitles background", panel, Vector2.zero, Vector2.zero,
                Vector2.zero, new Vector2(1920, 180));
            ArchiveUi.Paint(bottom, new Color(0, 0, 0, 0.85f));
            openingCaption = WrappedText(bottom, "", new Vector2(240, -28), new Vector2(1440, 116), 32);
            openingCaption.alignment = TextAlignmentOptions.Center;
            openingLocation = ArchiveUi.Text(panel, "Parkville · 150 years from now", new Vector2(60, -98),
                new Vector2(700, 44), 26).gameObject;
            openingFade = ArchiveUi.Paint(ArchiveUi.Stretch("Fade", panel), Color.black);
            var skip = ArchiveUi.Button(panel, "Skip Intro", -92, SkipIntro, ArchiveUi.Copper);
            ArchiveUi.Place(skip.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-44, -92), new Vector2(300, 54));
        }

        private void UpdateOpening(string caption, float fade, bool location)
        {
            if (screen != Screen.Opening) return;
            if (openingCaptionShot != opening.CurrentShotIndex)
            {
                openingCaptionShot = opening.CurrentShotIndex;
                PlayStoryText(openingCaption, caption);
            }
            openingFade.color = new Color(0, 0, 0, fade);
            openingLocation.SetActive(location);
        }

        public void SkipIntro()
        {
            if (screen != Screen.Opening) return;
            if (opening != null) opening.Skip(); else BeginTutorial(false);
        }

        private void OpeningCompleted()
        {
            if (opening != null)
            {
                opening.Completed -= OpeningCompleted;
                opening.PresentationChanged -= UpdateOpening;
                Destroy(opening.gameObject);
                opening = null;
            }
            if (isActiveAndEnabled && !loading && screen == Screen.Opening) BeginTutorial(false);
        }

        public void ReplayTutorial()
        {
            if (screen == Screen.Pause) BeginTutorial(true);
        }

        private void BeginTutorial(bool replay)
        {
            combatTutorial = false;
            tutorialReplay = replay;
            tutorialPage = 0;
            Show(Screen.Tutorial);
        }

        // Enemy activation only checks the current screen; it never opens a guide.
        public bool TryBeginCombat() => screen == Screen.Playing;

        public bool TryShowCombatTutorialAfterQuestAccepted()
        {
            if (PuzzleRunState.CombatTutorialShown) return false;
            if (screen != Screen.Playing || inputReader.IsGameplayBlocked || puzzleScene) return false;
            if (Config?.Story.combatTutorialGuide == null)
            {
                Debug.LogError("GameplayConfig combat tutorial is missing.", this);
                return false;
            }
            combatTutorial = true;
            tutorialReplay = false;
            tutorialPage = 0;
            Show(Screen.Tutorial);
            return true;
        }

        public void TurnTutorialPage(int delta)
        {
            if (screen != Screen.Tutorial || Guide == null) return;
            tutorialPage = Mathf.Clamp(tutorialPage + delta, 0, TutorialPages.Length - 1);
            Show(Screen.Tutorial);
        }

        public void FinishTutorial()
        {
            if (screen != Screen.Tutorial) return;
            if (combatTutorial) PuzzleRunState.CompleteCombatTutorial();
            combatTutorial = false;
            Show(tutorialReplay ? Screen.Pause : Screen.Playing);
            SaveGameService.RequestSave();
        }

        private void BuildTutorial()
        {
            panel = ArchiveUi.Stretch("Tutorial", overlay);
            var content = ArchiveUi.Rect("Tutorial content", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1540, 900));
            ArchiveUi.Frame(content, ArchiveUi.Copper);
            var guide = Guide;
            var pages = TutorialPages;
            if (pages == null || pages.Length == 0)
            {
                Debug.LogError("Tutorial guide is missing.", this);
                WrappedText(content, "Tutorial unavailable. Open How to Play from the pause menu to retry.",
                    new Vector2(48, -90), new Vector2(1400, 120), 30);
                ArchiveUi.Button(content, "Begin", -760, FinishTutorial, ArchiveUi.Copper);
                return;
            }
            var page = pages[tutorialPage];
            ArchiveUi.Text(content, page.title, new Vector2(40, -26), new Vector2(1280, 62), 40,
                fontRole: ArchiveUi.FontRole.Heading);
            ArchiveUi.Text(content, $"{tutorialPage + 1} / {pages.Length}", new Vector2(705, -816), new Vector2(130, 42), 26,
                TextAlignmentOptions.Center, ArchiveUi.FontRole.Technical);
            var picture = ArchiveUi.Rect("Gameplay demonstration", content, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(40, -104), new Vector2(960, 540));
            var sidebar = ArchiveUi.Rect("Tutorial explanation", content, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(1032, -104), new Vector2(468, 650));
            var instruction = ArchiveUi.Text(sidebar, page.instruction, Vector2.zero, new Vector2(468, 160), 29,
                TextAlignmentOptions.TopLeft);
            instruction.name = "Tutorial instruction";
            instruction.textWrappingMode = TextWrappingModes.Normal;
            var controls = ArchiveUi.Rect("Demonstrated input", sidebar, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(84, -176), new Vector2(300, 380));
            if (!string.IsNullOrEmpty(page.note))
            {
                var note = ArchiveUi.Text(sidebar, page.note, new Vector2(0, -570), new Vector2(468, 80), 21,
                    TextAlignmentOptions.TopLeft);
                note.name = "Tutorial reminder";
                note.textWrappingMode = TextWrappingModes.Normal;
                note.color = new Color(.72f, .75f, .73f);
            }
            panel.gameObject.AddComponent<TutorialDemoPlayer>().Initialize(page, picture, controls, instruction);
            var previous = ArchiveUi.Button(content, "Previous", -810, () => TurnTutorialPage(-1), ArchiveUi.Copper);
            previous.GetComponent<RectTransform>().anchoredPosition = new Vector2(-400, -810);
            previous.interactable = tutorialPage > 0;
            var close = ArchiveUi.Button(panel, tutorialReplay ? "Close" : "Skip Tutorial", -92, FinishTutorial, ArchiveUi.Copper);
            ArchiveUi.Place(close.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-44, -92), new Vector2(300, 54));
            var next = ArchiveUi.Button(content, tutorialPage == pages.Length - 1 ? (tutorialReplay ? "Done" : "Begin") : "Next",
                -810, () => { if (tutorialPage == pages.Length - 1) FinishTutorial(); else TurnTutorialPage(1); }, ArchiveUi.Copper);
            next.GetComponent<RectTransform>().anchoredPosition = new Vector2(400, -810);
            ArchiveUi.Focus(next);
        }

        public bool ShowArchiveRecord(int index, System.Action continueAction)
        {
            if (screen != Screen.Playing || health.IsDead || archiveContinue != null) return false;
            archiveTitle = $"Reference record {index} / 3";
            archiveRecord = null;
            if (Config != null && Config.Quests != null
                && Config.Quests.TryGetPuzzleDefinition(index, out var definition))
                archiveRecord = definition.ArchiveRecord(index);
            if (string.IsNullOrWhiteSpace(archiveRecord))
                archiveRecord = "Reference record recovered.";
            archiveContinue = continueAction;
            Show(Screen.ArchiveRecord);
            return true;
        }

        private void BuildArchiveRecord()
        {
            panel = ArchiveUi.Rect("ArchiveRecord", overlay, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(880, 540));
            ArchiveUi.Frame(panel, ArchiveUi.Copper);
            ArchiveUi.Text(panel, archiveTitle, new Vector2(48, -50), new Vector2(784, 66), 36,
                fontRole: ArchiveUi.FontRole.Heading);
            var record = WrappedText(panel, "", new Vector2(48, -160), new Vector2(784, 210), 30);
            PlayStoryText(record, archiveRecord);
            ArchiveUi.Focus(ArchiveUi.Button(panel, "Continue", -426, ContinueArchiveRecord, ArchiveUi.Copper));
        }

        public void ContinueArchiveRecord()
        {
            if (screen != Screen.ArchiveRecord || archiveContinue == null) return;
            if (storyTypewriter != null && storyTypewriter.Complete()) return;
            var action = archiveContinue;
            archiveContinue = null;
            Caffeine.Gameplay.Persistence.SaveGameService.RequestSave();
            Show(Screen.Playing);
            action();
        }

        private void OnPlayerDied(HealthShield source) => Show(Screen.GameOver);

        private void PlayStoryText(TMP_Text label, string text)
        {
            storyTypewriter = label.GetComponent<TypewriterText>();
            if (storyTypewriter == null) storyTypewriter = label.gameObject.AddComponent<TypewriterText>();
            storyTypewriter.Play(text, Config != null ? Config.Story.TypewriterCharactersPerSecond : 35f);
        }

        private void ContinueVictory()
        {
            if (screen != Screen.Victory || loading) return;
            if (storyTypewriter != null && storyTypewriter.Complete()) return;
            ReturnToTitle();
        }

        public void Respawn()
        {
            if (loading || screen != Screen.GameOver || !deathController.TryRespawn()) return;
            archiveContinue = null;
            Caffeine.Gameplay.Persistence.SaveGameService.RequestSave();
            Show(Screen.Playing);
        }

        public void ShowVictory()
        {
            if (health == null || health.IsDead || screen == Screen.GameOver || screen == Screen.Title) return;
            PuzzleRunState.CompleteBoss();
            Show(Screen.Victory);
            Caffeine.Gameplay.Persistence.SaveGameService.RequestSave();
        }

        private void ReturnToTitle()
        {
            if (loading) return;
            loading = true;
            StartCoroutine(ReturnToTitleSaved());
        }

        private TMP_Text saveLabel;
        private void ConfirmNewGame()
        {
            if (!SaveGameService.Instance.HasStoredData) { SaveGameService.Instance.StartNewGame(); return; }
            Show(Screen.Title);
            foreach (var button in panel.GetComponentsInChildren<UnityEngine.UI.Button>()) button.gameObject.SetActive(false);
            Caption("Replace the existing save?", -172);
            var confirm = AddButton("Replace Save", -260, () => SaveGameService.Instance.StartNewGame(), ArchiveUi.Red);
            AddButton("Cancel", -340, () => Show(Screen.Title), ArchiveUi.Copper);
            ArchiveUi.Focus(confirm);
        }
        public string CaptureTutorial() => screen == Screen.Tutorial && !tutorialReplay ? (combatTutorial ? "combat" : puzzleScene ? "puzzle" : "basic") : screen == Screen.Opening ? "basic" : null;
        public void RestoreSaveScreen(SaveGame s)
        {
            loading=false;
            if(s.vitals.IsDead) Show(Screen.GameOver);
            else if(s.run.BossDefeated || s.boss?.vitals.IsDead == true) Show(Screen.Victory);
            else if(!string.IsNullOrEmpty(s.tutorial)) { combatTutorial=s.tutorial=="combat"; tutorialReplay=false; tutorialPage=0; Show(Screen.Tutorial); }
            else Show(Screen.Pause);
        }
        public void TravelFailed() { loading=false; Show(Screen.Pause); }
        private IEnumerator ReturnToTitleSaved()
        {
            yield return SaveGameService.Instance.Flush();
            if(SaveGameService.Instance.Status.StartsWith("Save failed"))
            {
                loading=false; Show(Screen.Pause);
                AddButton("Return Without Saving", -660, ReturnWithoutSaving, ArchiveUi.Red);
                yield break;
            }
            ReturnWithoutSaving();
        }
        private void ReturnWithoutSaving()
        {
            SaveGameService.Instance.EndRun();
            PlayerPrefs.Save(); PuzzleRunState.ResetRun(); SceneManager.LoadSceneAsync("StartScene");
        }

    }
}
