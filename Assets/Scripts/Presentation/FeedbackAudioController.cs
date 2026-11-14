using Caffeine.Gameplay.Config;
using UnityEngine;

namespace Caffeine.Gameplay.Presentation
{
    public enum FeedbackSound { Navigate, Confirm, Open, Close, Purchase, Reject, Coin }

    [DisallowMultipleComponent]
    public sealed class FeedbackAudioController : MonoBehaviour
    {
        private static FeedbackAudioController instance;
        private readonly AudioSource[] voices = new AudioSource[4];
        private float nextNavigationAt;
        private float nextCoinAt;
        private int nextUiVoice;
        private int previousCoin = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance() => instance = null;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            for (int index = 0; index < voices.Length; index++)
            {
                var channel = new GameObject(index == 3 ? "Coin voice" : $"UI voice {index + 1}");
                channel.transform.SetParent(transform, false);
                var voice = channel.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.loop = false;
                voice.spatialBlend = 0f;
                voice.dopplerLevel = 0f;
                voice.ignoreListenerPause = true;
                voice.priority = 24;
                voices[index] = voice;
            }
        }

        public static void Play(FeedbackSound sound)
        {
            if (!Application.isPlaying || ConfigSystem.Instance == null)
            {
                return;
            }

            if (instance == null)
            {
                new GameObject("Feedback Audio").AddComponent<FeedbackAudioController>();
            }

            instance.PlayVoice(sound, ConfigSystem.Instance.Config.FeedbackAudio);
        }

        private void PlayVoice(FeedbackSound sound, GameplayConfig.FeedbackAudioSettings settings)
        {
            if (sound == FeedbackSound.Navigate && Time.unscaledTime < nextNavigationAt
                || sound == FeedbackSound.Coin && Time.unscaledTime < nextCoinAt)
            {
                return;
            }

            AudioClip clip = sound switch
            {
                FeedbackSound.Navigate => settings.Navigate,
                FeedbackSound.Confirm => settings.Confirm,
                FeedbackSound.Open => settings.Open,
                FeedbackSound.Close => settings.Close,
                FeedbackSound.Purchase => settings.Purchase,
                FeedbackSound.Reject => settings.Reject,
                _ => null
            };
            bool coin = sound == FeedbackSound.Coin;
            if (coin && settings.Coins.Length > 0)
            {
                int variant = Random.Range(0, settings.Coins.Length);
                if (settings.Coins.Length > 1 && variant == previousCoin)
                {
                    variant = (variant + 1) % settings.Coins.Length;
                }

                previousCoin = variant;
                clip = settings.Coins[variant];
            }

            if (clip == null)
            {
                return;
            }

            // Reserve the last channel so a coin burst cannot cut off a UI cue.
            var source = voices[coin ? 3 : nextUiVoice++ % 3];
            source.Stop();
            source.clip = clip;
            source.volume = coin ? settings.CoinVolume : settings.UiVolume;
            if (sound == FeedbackSound.Navigate)
            {
                source.volume *= settings.NavigationVolumeMultiplier;
                nextNavigationAt = Time.unscaledTime + settings.NavigationInterval;
            }

            source.pitch = coin ? Random.Range(settings.CoinPitch.x, settings.CoinPitch.y) : 1f;
            source.Play();
            if (coin)
            {
                nextCoinAt = Time.unscaledTime + settings.CoinInterval;
            }
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }
    }
}
