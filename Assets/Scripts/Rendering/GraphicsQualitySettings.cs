using System;
using UnityEngine;

namespace Caffeine.Gameplay.Rendering
{
    public enum GraphicsQuality { Low, Medium, High }

    /// <summary>Player graphics preferences, independent of run saves and scene lifetime.</summary>
    public static class GraphicsQualitySettings
    {
        public const string PreferenceKey = "Archive.GraphicsQuality";
        public static GraphicsQuality Current { get; private set; } = GraphicsQuality.High;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void LoadSavedQuality()
        {
            string saved = PlayerPrefs.GetString(PreferenceKey, nameof(GraphicsQuality.High));
            var quality = saved switch
            {
                nameof(GraphicsQuality.Low) => GraphicsQuality.Low,
                nameof(GraphicsQuality.Medium) => GraphicsQuality.Medium,
                _ => GraphicsQuality.High
            };
            Apply(quality, false);
        }

        public static bool SetQuality(GraphicsQuality quality) => Apply(quality, true);

        private static bool Apply(GraphicsQuality quality, bool save)
        {
            if (quality < GraphicsQuality.Low || quality > GraphicsQuality.High) return false;
            int index = Array.IndexOf(QualitySettings.names, quality.ToString());
            if (index < 0)
            {
                Debug.LogError($"Graphics quality preset '{quality}' is missing from Project Settings.");
                return false;
            }

            QualitySettings.SetQualityLevel(index, true);
            Current = quality;
            if (save)
            {
                PlayerPrefs.SetString(PreferenceKey, quality.ToString());
                PlayerPrefs.Save();
            }
            return true;
        }
    }
}
