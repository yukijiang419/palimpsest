using System;
using UnityEngine;

namespace Caffeine.Gameplay.UI
{
    [CreateAssetMenu(menuName = "Caffeine/Tutorial Guide")]
    public sealed class TutorialGuide : ScriptableObject
    {
        [Flags]
        public enum Controls
        {
            None = 0, W = 1, A = 2, S = 4, D = 8, Shift = 16,
            E = 32, MouseLeft = 64, MouseMove = 128, T = 256, R = 512,
            MouseRight = 1024, G = 2048, X = 4096, Escape = 8192,
            F = 16384, Q = 32768, MouseMiddle = 131072
        }

        [Serializable]
        public sealed class Cue
        {
            [Min(0)] public float start;
            [Min(0)] public float end;
            public Controls controls;
            public string caption;
            // Coordinates are normalized to the video, measured from its top-left.
            public bool showPointer;
            public Vector2 pointer;
            public Rect highlight;
        }

        [Serializable]
        public sealed class Page
        {
            public string title;
            public Sprite screenshot;
            public string key;
            [TextArea(2, 4)] public string instruction;
            [Tooltip("Relative to StreamingAssets, e.g. Tutorials/move.mp4")]
            public string videoPath;
            public Controls visibleControls;
            [TextArea(1, 3)] public string note;
            public Cue[] cues = Array.Empty<Cue>();

            public Cue CueAt(double time)
            {
                if (cues == null) return null;
                foreach (var cue in cues)
                    if (cue != null && time >= cue.start && time < cue.end) return cue;
                return null;
            }
        }

        public Page[] pages;
    }
}
