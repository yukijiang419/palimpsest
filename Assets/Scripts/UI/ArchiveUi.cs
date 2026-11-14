using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Caffeine.Gameplay.UI
{
    // Shared archive instrument controls. All measurements use a 1920 x 1080 canvas.
    public static class ArchiveUi
    {
        public enum FontRole { Body, Heading, Button, Technical }

        private static readonly TMP_FontAsset[] fonts = new TMP_FontAsset[4];
        private static readonly string[] fontNames = {
            "IBMPlexSans-Regular SDF", "SourceSerif4Subhead-Semibold SDF",
            "IBMPlexSans-Medium SDF", "IBMPlexMono-Medium SDF"
        };

        public static TMP_FontAsset Font(FontRole role)
        {
            int index = (int)role;
            if (fonts[index] == null) fonts[index] = Resources.Load<TMP_FontAsset>("UI/Fonts/" + fontNames[index]);
            return fonts[index];
        }

        public static void ApplyFont(TMP_Text label, FontRole role)
        {
            label.font = Font(role);
            label.fontStyle &= ~FontStyles.Bold;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeFonts()
        {
            TMP_Settings.defaultFontAsset = Font(FontRole.Body);
            SceneManager.sceneLoaded -= ApplySavedFonts;
            SceneManager.sceneLoaded += ApplySavedFonts;
        }

        // Older scene instances retain serialized TMP references. Upgrade these before the first rendered frame.
        private static void ApplySavedFonts(Scene scene, LoadSceneMode mode)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var label in root.GetComponentsInChildren<TMP_Text>(true))
                    if (label.font == null || label.font.name.StartsWith("LiberationSans"))
                        ApplyFont(label, label is TextMeshPro ? FontRole.Technical : FontRole.Body);
        }

        public static readonly Color Copper = new(0.68f, 0.51f, 0.29f);
        public static readonly Color Paper = new(0.92f, 0.91f, 0.85f);
        public static readonly Color Panel = new(0.078f, 0.169f, 0.161f, 0.98f);
        public static readonly Color Ink = new(0.065f, 0.095f, 0.09f);
        public static readonly Color Red = new(0.84f, 0.25f, 0.23f);
        public static readonly Color Cyan = new(0.29f, 0.77f, 0.72f);
        public static readonly Color Shield = new(0.25f, 0.67f, 0.78f);
        private static readonly Dictionary<string, Sprite> sprites = new();

        public static Sprite Sprite(string name)
        {
            if (sprites.TryGetValue(name, out var cached) && cached != null) return cached;
            var loaded = Resources.LoadAll<Sprite>("UI/TemporalArchive/" + name);
            var sprite = loaded.Length > 0 ? loaded[0] : null;
            sprites[name] = sprite;
            return sprite;
        }

        public static Image Surface(RectTransform rect, string name, bool blocksInput = false)
        {
            var image = Paint(rect, Color.white, blocksInput);
            image.sprite = Sprite(name);
            image.type = image.sprite != null && image.sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            image.preserveAspect = false;
            return image;
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5;
            rect.SetParent(parent, false);
            Place(rect, anchor, pivot, position, size);
            return rect;
        }

        public static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        public static RectTransform Stretch(string name, Transform parent)
        {
            var rect = Rect(name, parent, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            rect.anchorMax = Vector2.one;
            return rect;
        }

        public static Image Paint(RectTransform rect, Color color, bool blocksInput = false)
        {
            var image = rect.GetComponent<Image>();
            if (image == null) image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = blocksInput;
            return image;
        }

        public static void Frame(RectTransform rect, Color accent)
        {
            HidePrototypeRules(rect);
            Surface(rect, "panel");
        }

        public static void ClearFrame(RectTransform rect)
        {
            HidePrototypeRules(rect);
            var image = rect.GetComponent<Image>();
            if (image != null) { image.sprite = null; image.color = Color.clear; image.raycastTarget = false; }
        }

        private static void HidePrototypeRules(Transform rect)
        {
            foreach (string name in new[] { "Top rule", "Bottom rule", "Left rule", "Right rule", "Coin divider" })
                if (rect.Find(name) != null) rect.Find(name).gameObject.SetActive(false);
        }

        public static void StyleButton(Button button, bool card = false)
        {
            Surface((RectTransform)button.transform, card ? "panel" : "button", true);
            button.targetGraphic = button.GetComponent<Image>();
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (!card && label != null) ApplyFont(label, FontRole.Button);
            var visual = button.GetComponent<ArchiveButtonVisual>();
            if (visual == null) visual = button.gameObject.AddComponent<ArchiveButtonVisual>();
            visual.Configure(card);
        }

        public static void Focus(Button button)
        {
            if (EventSystem.current != null && button != null)
                EventSystem.current.SetSelectedGameObject(button.gameObject);
        }

        public static RectTransform Prompt(Transform parent, string key, Vector2 position, Vector2 size, out TMP_Text label)
        {
            var root = Rect("Interaction prompt", parent, new Vector2(.5f, 0), new Vector2(.5f, 0), position, size);
            Surface(root, "button");
            var cap = Rect("Input key", root, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(18, 0), new Vector2(46, 46));
            Surface(cap, "keycap");
            Text(cap, key, Vector2.zero, cap.sizeDelta, 24, TextAlignmentOptions.Center, FontRole.Technical);
            label = Text(root, "", new Vector2(80, 0), new Vector2(size.x - 104, size.y), 23);
            label.textWrappingMode = TextWrappingModes.Normal;
            return root;
        }

        public static void Crest(Transform parent, Vector2 position, float size)
        {
            var plate = Rect("University badge", parent, new Vector2(0, 1), new Vector2(0, 1), position, Vector2.one * size);
            Surface(plate, "node-start");
            var crest = Rect("University crest", plate, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                Vector2.zero, new Vector2(size * .65f, size * .59f));
            var image = Surface(crest, "unimelb-crest");
            image.preserveAspect = true;
        }

        private static void Edge(string name, Transform parent, Vector2 min, Vector2 max, Color color)
        {
            var rect = Stretch(name, parent);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = min.x == max.x ? new Vector2(1, 0) : new Vector2(0, 1);
            Paint(rect, color);
            rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        }

        public static void Outline(RectTransform rect, Color color)
        {
            Edge("Top edge", rect, new Vector2(0, 1), Vector2.one, color);
            Edge("Bottom edge", rect, Vector2.zero, new Vector2(1, 0), color);
            Edge("Left edge", rect, Vector2.zero, new Vector2(0, 1), color);
            Edge("Right edge", rect, new Vector2(1, 0), Vector2.one, color);
        }

        public static TMP_Text Text(Transform parent, string text, Vector2 position, Vector2 size,
            float fontSize = 22, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft,
            FontRole fontRole = FontRole.Body)
        {
            var rect = Rect(text, parent, new Vector2(0, 1), new Vector2(0, 1), position, size);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            ApplyFont(label, fontRole);
            label.fontSize = fontSize;
            label.enableAutoSizing = false;
            label.color = Paper;
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            return label;
        }

        public static Image Bar(Transform parent, string name, Vector2 position, float width, Color color)
        {
            var track = Rect(name, parent, new Vector2(0, 1), new Vector2(0, 1), position, new Vector2(width, 18));
            var fill = Paint(Stretch("Fill", track), color);
            SkinBar(fill, name.Contains("Stamina") || name.Contains("shield") ? "stamina-track" : "health-track");
            return fill;
        }

        public static RectTransform BarTrack(Image fill) => (RectTransform)(fill.transform.parent.name == "Bar well"
            ? fill.transform.parent.parent : fill.transform.parent);

        public static void SkinBar(Image fill, string skin)
        {
            var track = BarTrack(fill);
            Surface(track, skin);
            var well = track.Find("Bar well") as RectTransform;
            if (well == null)
            {
                well = Stretch("Bar well", track);
                well.anchorMin = new Vector2(.05f, .24f);
                well.anchorMax = new Vector2(.95f, .76f);
                fill.transform.SetParent(well, false);
                fill.rectTransform.anchorMin = Vector2.zero;
                fill.rectTransform.anchorMax = Vector2.one;
                fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            }
            fill.sprite = null;
            fill.type = Image.Type.Simple;
        }

        public static void SetBar(Image fill, float current, float maximum)
        {
            fill.rectTransform.anchorMax = new Vector2(maximum > 0 ? Mathf.Clamp01(current / maximum) : 0, 1);
        }

        public static Button Button(Transform parent, string title, float y, UnityAction action, Color accent)
        {
            var rect = Rect(title, parent, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(0, y), new Vector2(300, 54));
            var background = Surface(rect, "button");
            background.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            Text(rect, title, Vector2.zero, new Vector2(300, 54), 24, TextAlignmentOptions.Center, FontRole.Button);
            StyleButton(button);
            button.onClick.AddListener(action);
            return button;
        }
    }
}
