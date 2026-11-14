using UnityEngine;

namespace Caffeine.Gameplay.Config
{
    [DefaultExecutionOrder(-1000)]
    public sealed class ConfigSystem : MonoBehaviour
    {
        public static ConfigSystem Instance { get; private set; }

        [field: SerializeField] public GameplayConfig Config { get; private set; }

        private void Awake()
        {
            // keep one config system
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            if (Config == null)
            {
                enabled = false;
                throw new System.InvalidOperationException("ConfigSystem requires a GameplayConfig asset.");
            }

            string error = "Quest settings are missing.";
            if (Config.Quests == null || !Config.Quests.TryValidate(out error))
            {
                enabled = false;
                throw new System.InvalidOperationException($"Invalid quest configuration: {error}");
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
