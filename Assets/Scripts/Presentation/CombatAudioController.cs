using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Combat;
using Caffeine.Gameplay.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Caffeine.Gameplay.Presentation
{
    public enum CombatSound
    {
        Swing, Impact, ShieldHit, ShieldBreak, PlayerHurt,
        EnemyAlert, EnemyAttack, EnemyShot, EnemyHurt, EnemyDeath
    }

    [DisallowMultipleComponent]
    public sealed class CombatAudioController : MonoBehaviour
    {
        private GameplayConfig.FeedbackAudioSettings Settings => ConfigSystem.Instance.Config.FeedbackAudio;
        private const int VoiceLimit = 16;
        private static CombatAudioController instance;
        private readonly AudioSource[] voices = new AudioSource[VoiceLimit];
        private readonly AudioClip[][] clips = new AudioClip[10][];
        private readonly float[] nextAllowedAt = new float[10];
        private readonly int[] previousVariant = new int[10];
        private TutorialPresenter menus;
        private GameTimeController timeController;
        private bool paused;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance() => instance = null;

        private void Awake()
        {
            instance = this;
            BindScene();
            SceneManager.sceneLoaded += OnSceneLoaded;
            for (int index = 0; index < clips.Length; index++)
            {
                clips[index] = Resources.LoadAll<AudioClip>($"Audio/Combat/{(CombatSound)index}");
                previousVariant[index] = -1;
                if (clips[index].Length == 0)
                {
                    Debug.LogError($"Missing combat sound: {(CombatSound)index}", this);
                }
            }

            // keep audio separate from enemies so death sounds can finish after destruction
            for (int index = 0; index < voices.Length; index++)
            {
                var channel = new GameObject($"Combat voice {index + 1}");
                channel.transform.SetParent(transform, false);
                var voice = channel.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.loop = false;
                voice.dopplerLevel = 0f;
                voice.rolloffMode = AudioRolloffMode.Linear;
                voice.minDistance = Settings.CombatMinDistance;
                voice.maxDistance = Settings.CombatMaxDistance;
                voices[index] = voice;
            }
        }

        private void BindScene()
        {
            menus = FindFirstObjectByType<TutorialPresenter>();
            timeController = FindFirstObjectByType<GameTimeController>();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => BindScene();

        private bool MenuOpen => menus != null && menus.IsShowing
            || timeController != null && timeController.IsShopPaused;

        private void Update()
        {
            // let the final hit sound finish under the Game Over or Victory screen
            bool shouldPause = menus != null && menus.IsShowing && menus.IsGameplayActive
                || timeController != null && timeController.IsShopPaused;
            if (shouldPause == paused)
            {
                return;
            }

            paused = shouldPause;
            foreach (var voice in voices)
            {
                if (paused)
                {
                    voice.Pause();
                }
                else
                {
                    voice.UnPause();
                }
            }
        }

        public static void Play(CombatSound sound, Vector3 position, bool spatial = true)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (instance == null)
            {
                instance = new GameObject("Combat Audio").AddComponent<CombatAudioController>();
            }

            if (instance.MenuOpen || instance.paused)
            {
                return;
            }

            instance.PlayVoice(sound, position, spatial);
        }

        public static void PlayDamage(HealthShield target, DamageResult result, float previousShield)
        {
            if (!result.HasEffect)
            {
                return;
            }

            Vector3 position = target.transform.position + Vector3.up;
            if (target.Faction == Faction.Player && result.HealthLoss > 0f)
            {
                Play(CombatSound.PlayerHurt, position, false);
                return;
            }

            if (result.ShieldLoss > 0f)
            {
                Play(previousShield > 0f && target.CurrentShield <= 0f
                                    ? CombatSound.ShieldBreak : CombatSound.ShieldHit, position,
                    target.Faction != Faction.Player);
            }

            if (result.HealthLoss > 0f)
            {
                Play(CombatSound.Impact, position);
                Play(target.CurrentHealth <= 0f ? CombatSound.EnemyDeath : CombatSound.EnemyHurt, position);
            }
        }

        private void PlayVoice(CombatSound sound, Vector3 position, bool spatial)
        {
            int cue = (int)sound;
            if (Time.unscaledTime < nextAllowedAt[cue] || clips[cue].Length == 0)
            {
                return;
            }

            int priority = sound == CombatSound.PlayerHurt || sound == CombatSound.ShieldBreak ? 32
                            : sound == CombatSound.EnemyAlert || sound == CombatSound.EnemyHurt ? 96 : 48;
            AudioSource source = null;
            foreach (var voice in voices)
            {
                if (!voice.isPlaying)
                {
                    source = voice;
                    break;
                }
                if (voice.priority >= priority && (source == null || voice.priority > source.priority))
                {
                    source = voice;
                }
            }
            if (source == null)
            {
                return;
            }

            int variant = Random.Range(0, clips[cue].Length);
            if (clips[cue].Length > 1 && variant == previousVariant[cue])
            {
                variant = (variant + 1) % clips[cue].Length;
            }

            previousVariant[cue] = variant;
            source.Stop();
            source.transform.position = position;
            source.clip = clips[cue][variant];
            source.priority = priority;
            source.spatialBlend = spatial ? 1f : 0f;
            source.pitch = Random.Range(Settings.CombatPitch.x, Settings.CombatPitch.y);
            source.volume = Settings.CombatVolumes[cue];
            source.Play();
            nextAllowedAt[cue] = Time.unscaledTime + Settings.CombatIntervals[cue];
        }

        private void OnDisable()
        {
            foreach (var voice in voices)
            {
                if (voice != null)
                {
                    voice.Stop();
                }
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (instance == this)
            {
                instance = null;
            }
        }
    }
}
