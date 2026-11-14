using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Enemy;
using Caffeine.Gameplay.Player;
using Caffeine.Gameplay.Puzzles;
using Caffeine.Gameplay.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Caffeine.Gameplay.Presentation
{
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class BackgroundMusicController : MonoBehaviour
    {
        public enum MusicMood { Exploration, Puzzle, Combat }

        private GameplayConfig.FeedbackAudioSettings Settings => ConfigSystem.Instance.Config.FeedbackAudio;

        private readonly AudioSource[] sources = new AudioSource[2];
        private readonly float[] gains = new float[2];
        private AudioClip explorationClip;
        private AudioClip puzzleClip;
        private AudioClip combatClip;
        private AudioClip requestedClip;
        private PlayerInputReader player;
        private EnemySystem enemies;
        private TutorialPresenter menus;
        private GameTimeController timeController;
        private TimelinePuzzleArea[] puzzles;
        private int activeSource = -1;
        private float nextStateCheck;
        private float menuGain = 1f;
        private float combatEndsAt;

        public MusicMood CurrentMood { get; private set; }
        public AudioClip CurrentClip => requestedClip;

        // also handle standalone puzzle scenes and returning to the title
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSubscription() => SceneManager.sceneLoaded -= OnSceneLoaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (FindFirstObjectByType<BackgroundMusicController>() != null
                || FindFirstObjectByType<PlayerInputReader>() == null)
            {
                return;
            }

            var root = new GameObject("Background Music");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<BackgroundMusicController>();
        }

        private void Awake()
        {
            explorationClip = Resources.Load<AudioClip>("Audio/Music/SRG774/Sector");
            puzzleClip = Resources.Load<AudioClip>("Audio/Music/SRG774/Airy");
            combatClip = Resources.Load<AudioClip>("Audio/Music/Blackvoid6/Cyberfighter_loop");
            if (explorationClip == null || puzzleClip == null || combatClip == null)
            {
                Debug.LogError("A background music resource is missing.", this);
            }

            for (int index = 0; index < sources.Length; index++)
            {
                var channel = new GameObject($"Music {index + 1}");
                channel.transform.SetParent(transform, false);
                var source = channel.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = true;
                source.spatialBlend = 0f;
                source.dopplerLevel = 0f;
                source.priority = 16;
                source.ignoreListenerPause = true;
                source.volume = 0f;
                sources[index] = source;
            }
        }

        private void Start()
        {
            BindScene();
            SceneManager.sceneLoaded += RefreshScene;
        }

        private void BindScene()
        {
            player = FindFirstObjectByType<PlayerInputReader>();
            enemies = FindFirstObjectByType<EnemySystem>();
            menus = FindFirstObjectByType<TutorialPresenter>();
            timeController = FindFirstObjectByType<GameTimeController>();
            puzzles = FindObjectsByType<TimelinePuzzleArea>(FindObjectsSortMode.None);
            nextStateCheck = 0f;
        }

        private void RefreshScene(Scene scene, LoadSceneMode mode) => BindScene();

        private void Update()
        {
            if (Time.unscaledTime >= nextStateCheck)
            {
                nextStateCheck = Time.unscaledTime + Settings.MusicStateInterval;
                SelectMusic();
            }

            bool menuOpen = menus != null && menus.IsShowing
                || timeController != null && timeController.IsShopPaused;
            menuGain = Mathf.MoveTowards(menuGain, menuOpen ? Settings.MenuVolumeMultiplier : 1f,
                Time.unscaledDeltaTime * Settings.MenuFadeSpeed);
            for (int index = 0; index < sources.Length; index++)
            {
                gains[index] = Mathf.MoveTowards(gains[index], index == activeSource ? 1f : 0f,
                    Time.unscaledDeltaTime / Settings.MusicCrossfadeSeconds);
                sources[index].volume = gains[index] * Settings.MusicVolume * menuGain;
                if (index != activeSource && gains[index] == 0f && sources[index].isPlaying)
                {
                    sources[index].Stop();
                }
            }
        }

        private void SelectMusic()
        {
            MusicMood mood = MusicMood.Exploration;
            bool gameplay = menus == null || menus.IsGameplayActive;
            if (!gameplay)
            {
                combatEndsAt = 0f;
            }
            else
            {
                if (enemies != null)
                {
                    foreach (var enemy in enemies.OrderedTargets)
                    {
                        if (enemy != null && enemy.isActiveAndEnabled && enemy.IsFighting)
                        {
                            combatEndsAt = Time.unscaledTime + Settings.CombatMusicHoldSeconds;
                            break;
                        }
                    }
                }

                // keep combat music during Path Interaction and briefly after disengaging
                if (Time.unscaledTime < combatEndsAt)
                {
                    mood = MusicMood.Combat;
                }
                else if (player != null && puzzles != null)
                {
                    foreach (var puzzle in puzzles)
                    {
                        if (puzzle == null || !puzzle.isActiveAndEnabled || puzzle.Completed)
                        {
                            continue;
                        }

                        if (puzzle.ContainsPosition(player.transform.position))
                        {
                            mood = MusicMood.Puzzle;
                            break;
                        }
                    }
                }
            }

            CurrentMood = mood;
            AudioClip clip = mood == MusicMood.Combat ? combatClip
                : mood == MusicMood.Puzzle ? puzzleClip : explorationClip;
            if (clip == null)
            {
                clip = explorationClip;
            }

            if (clip == null || clip == requestedClip)
            {
                return;
            }

            requestedClip = clip;

            for (int index = 0; index < sources.Length; index++)
            {
                if (sources[index].clip == clip && sources[index].isPlaying)
                {
                    activeSource = index;
                    return;
                }
            }

            // reuse the quieter channel if the state changes during a fade
            activeSource = gains[0] <= gains[1] ? 0 : 1;
            AudioSource incoming = sources[activeSource];
            incoming.Stop();
            incoming.volume = 0f;
            gains[activeSource] = 0f;
            incoming.clip = clip;
            incoming.Play();
        }

        private void OnDisable()
        {
            for (int index = 0; index < sources.Length; index++)
            {
                if (sources[index] != null)
                {
                    sources[index].Stop();
                }

                gains[index] = 0f;
            }
            requestedClip = null;
            activeSource = -1;
        }

        private void OnDestroy() => SceneManager.sceneLoaded -= RefreshScene;
    }
}
