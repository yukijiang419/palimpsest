using System;
using System.Collections.Generic;
using TMPro;
using Caffeine.Gameplay.Config;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Caffeine.Gameplay.UI
{
    // Detach callbacks when leaving a page to ignore late video events.
    public sealed class TutorialDemoPlayer : MonoBehaviour
    {
        private sealed class KeyView
        {
            public TutorialGuide.Controls control;
            public RectTransform face;
            public Image fill;
            public TMP_Text label;
            public Vector2 position;
        }

        private readonly List<KeyView> keys = new();
        private const double TapHighlightSeconds = .2;
        private TutorialGuide.Page page;
        private VideoPlayer player;
        private RenderTexture texture;
        private RawImage picture;
        private RectTransform focus;
        private RectTransform pointer;
        private TMP_Text pointerLabel;
        private TutorialMouseGraphic mouse;
        private float prepareStarted;
        private bool disposed;
        private bool failed;
        // Pages without a recording replay their cues on a looping clock over the poster.
        private float scriptedStart = -1f;
        private float scriptedLength;
        public bool HasVideoFrame { get; private set; }
        public double PlaybackTime => player != null ? HasVideoFrame && player.frameRate > 0 && player.frame >= 0
            ? player.frame / player.frameRate : 0
            : scriptedStart >= 0f ? (Time.realtimeSinceStartup - scriptedStart) % scriptedLength : 0;
        public TutorialGuide.Controls DisplayedControls { get; private set; }
        public bool Failed => failed;
        public int CompletedLoops { get; private set; }

        public void Initialize(TutorialGuide.Page definition, RectTransform viewport,
            RectTransform controls, TMP_Text instruction)
        {
            page = definition;
            instruction.text = page.instruction;
            var fallback = ArchiveUi.Paint(ArchiveUi.Stretch("Demo poster", viewport), Color.white);
            fallback.sprite = page.screenshot;
            fallback.preserveAspect = true;
            if (page.screenshot == null && string.IsNullOrWhiteSpace(page.videoPath))
            {
                fallback.color = ArchiveUi.Ink;
            }
            picture = ArchiveUi.Stretch("Demo video", viewport).gameObject.AddComponent<RawImage>();
            picture.raycastTarget = false;
            picture.enabled = false;
            focus = ArchiveUi.Rect("Demo focus",
                viewport,
                new Vector2(0, 1),
                new Vector2(0, 1),
                Vector2.zero,
                Vector2.one);
            ArchiveUi.Outline(focus, ArchiveUi.Cyan);
            focus.gameObject.SetActive(false);
            pointer = ArchiveUi.Rect("Demo cursor",
                viewport,
                new Vector2(0, 1),
                new Vector2(.5f, .5f),
                Vector2.zero,
                new Vector2(34, 34));
            ArchiveUi.Surface(pointer, "keycap");
            pointerLabel = ArchiveUi.Text(pointer,
                "+",
                Vector2.zero,
                pointer.sizeDelta,
                30,
                TextAlignmentOptions.Center);
            pointer.gameObject.SetActive(false);
            BuildControls(controls);
            prepareStarted = Time.realtimeSinceStartup;
            if (string.IsNullOrWhiteSpace(page.videoPath))
            {
                FallBack();
                if (page.cues != null && page.cues.Length > 0)
                {
                    StartScriptedCues();
                }

                return;
            }

            texture = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32);
            texture.Create();
            picture.texture = texture;
            player = gameObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.source = VideoSource.Url;
            player.url = Application.streamingAssetsPath.TrimEnd('/',
                '\\') + "/" + page.videoPath.Replace(
                '\\',
                '/').TrimStart('/');
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = texture;
            player.audioOutputMode = VideoAudioOutputMode.None;
            player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            player.isLooping = true;
            player.waitForFirstFrame = true;
            player.sendFrameReadyEvents = true;
            player.prepareCompleted += Prepared;
            player.frameReady += FrameReady;
            player.errorReceived += Error;
            player.loopPointReached += Looped;
            player.Prepare();
        }

        private void BuildControls(RectTransform root)
        {
            int y = 0;
            var all = page.visibleControls;
            if ((all & (TutorialGuide.Controls.W | TutorialGuide.Controls.A | TutorialGuide.Controls.S | TutorialGuide.Controls.D)) != 0)
            {
                AddKey(root, TutorialGuide.Controls.W, "W", new Vector2(104, -y), 88);
                y += 88;
                AddKey(root, TutorialGuide.Controls.A, "A", new Vector2(4, -y), 88);
                AddKey(root, TutorialGuide.Controls.S, "S", new Vector2(104, -y), 88);
                AddKey(root, TutorialGuide.Controls.D, "D", new Vector2(204, -y), 88);
                y += 100;
            }
            if ((all & TutorialGuide.Controls.Shift) != 0)
            {
                AddKey(root, TutorialGuide.Controls.Shift, "SHIFT", new Vector2(4, -y), 288);
                y += 96;
            }
            // Single-letter keys share one centred row.
            var letters = new List<(TutorialGuide.Controls control, string label)>();
            if ((all & TutorialGuide.Controls.E) != 0)
            {
                letters.Add((TutorialGuide.Controls.E, "E"));
            }

            if ((all & TutorialGuide.Controls.T) != 0)
            {
                letters.Add((TutorialGuide.Controls.T, "T"));
            }

            if ((all & TutorialGuide.Controls.R) != 0)
            {
                letters.Add((TutorialGuide.Controls.R, "R"));
            }

            if ((all & TutorialGuide.Controls.G) != 0)
            {
                letters.Add((TutorialGuide.Controls.G, "G"));
            }

            if ((all & TutorialGuide.Controls.X) != 0)
            {
                letters.Add((TutorialGuide.Controls.X, "X"));
            }

            if ((all & TutorialGuide.Controls.F) != 0)
            {
                letters.Add((TutorialGuide.Controls.F, "F"));
            }

            if ((all & TutorialGuide.Controls.Q) != 0)
            {
                letters.Add((TutorialGuide.Controls.Q, "Q"));
            }

            for (int i = 0; i < letters.Count; i++)
            {
                AddKey(root,
                    letters[i].control,
                    letters[i].label,
                    new Vector2(104 + (i * 2 - (letters.Count - 1)) * 50, -y),
                    88);
            }

            if (letters.Count > 0)
            {
                y += 106;
            }

            if ((all & TutorialGuide.Controls.Escape) != 0)
            {
                AddKey(root, TutorialGuide.Controls.Escape, "ESC", new Vector2(4, -y), 288);
                y += 96;
            }
            if ((all & (TutorialGuide.Controls.MouseLeft | TutorialGuide.Controls.MouseRight | TutorialGuide.Controls.MouseMove
                | TutorialGuide.Controls.MouseMiddle)) != 0)
            {
                var mouseRect = ArchiveUi.Rect("Mouse diagram", root, new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(105, -y), new Vector2(90, 118));
                mouse = mouseRect.gameObject.AddComponent<TutorialMouseGraphic>();
                mouse.raycastTarget = false;
                var description = (all & TutorialGuide.Controls.MouseMiddle) != 0 ? "MIDDLE CLICK"
                    : (all & TutorialGuide.Controls.MouseRight) != 0 ? "HOLD RIGHT BUTTON"
                    : (all & TutorialGuide.Controls.MouseMove) != 0 ? "MOVE MOUSE" : "LEFT CLICK";
                ArchiveUi.Text(root,
                    description,
                    new Vector2(0, -y - 132),
                    new Vector2(300, 36),
                    22,
                    TextAlignmentOptions.Center);
            }
        }

        private void AddKey(RectTransform root,
            TutorialGuide.Controls control,
            string label,
            Vector2 position,
            float width)
        {
            var shadow = ArchiveUi.Rect(label + " shadow",
                root,
                new Vector2(0, 1),
                new Vector2(0, 1),
                position + Vector2.down * 7,
                new Vector2(width, 72));
            ArchiveUi.Paint(shadow, new Color(.015f, .018f, .018f));
            var face = ArchiveUi.Rect(label + " key",
                root,
                new Vector2(0, 1),
                new Vector2(0, 1),
                position,
                new Vector2(width, 72));
            ArchiveUi.Surface(face, width > 100 ? "button" : "keycap");
            var text = ArchiveUi.Text(face, label, Vector2.zero, face.sizeDelta, 30, TextAlignmentOptions.Center,
                ArchiveUi.FontRole.Technical);
            keys.Add(new KeyView { control = control,
                face = face,
                fill = face.GetComponent<Image>(),
                label = text,
                position = position });
        }

        private void Prepared(VideoPlayer source)
        {
            if (disposed || failed || !isActiveAndEnabled || source != player)
            {
                return;
            }

            source.Play();
        }

        private void FrameReady(VideoPlayer source, long frame)
        {
            if (disposed || failed || !isActiveAndEnabled || source != player)
            {
                return;
            }

            HasVideoFrame = true;
            picture.enabled = true;
        }

        private void Error(VideoPlayer source, string message)
        {
            if (disposed || source != player)
            {
                return;
            }

            Debug.LogWarning("Tutorial demonstration could not play: " + page.videoPath + ". " + message, this);
            FallBack();
        }

        private void Looped(VideoPlayer source)
        {
            if (disposed || source != player)
            {
                return;
            }

            CompletedLoops++;
            ApplyCue(null, false);
        }

        private void StartScriptedCues()
        {
            foreach (var cue in page.cues)
            {
                if (cue != null)
                {
                    scriptedLength = Mathf.Max(scriptedLength, cue.end);
                }
            }

            if (scriptedLength <= 0f)
            {
                return;
            }

            scriptedLength += 1f; // End each loop with every key released, like the recordings.
            scriptedStart = Time.realtimeSinceStartup;
        }

        private void Update()
        {
            if (!disposed && scriptedStart >= 0f)
            {
                ApplyCue(page.CueAt(PlaybackTime));
                return;
            }
            if (disposed || failed || player == null)
            {
                return;
            }

            if (!HasVideoFrame)
            {
                if (Time.realtimeSinceStartup - prepareStarted > ConfigSystem.Instance.Config.Story.VideoPrepareTimeout)
                {
                    FallBack();
                    return;
                }
            }
            // Sync input cues to video time, including decoder stalls.
            ApplyCue(HasVideoFrame ? page.CueAt(PlaybackTime) : null, HasVideoFrame);
        }

        // Derive flashes from the timeline, so even a skipped one-frame tap is visible.
        // Only colour persists after a tap; pose and DisplayedControls remain exact.
        private TutorialGuide.Controls HighlightedControlsAt(double time)
        {
            var highlighted = page.CueAt(time)?.controls ?? TutorialGuide.Controls.None;
            if (page.cues == null)
            {
                return highlighted;
            }

            TutorialGuide.Cue previous = null;
            foreach (var cue in page.cues)
            {
                if (cue == null)
                {
                    continue;
                }

                var before = previous != null && Math.Abs(previous.end - cue.start) < .001
                                    ? previous.controls : TutorialGuide.Controls.None;
                if (time >= cue.start && time < cue.start + TapHighlightSeconds)
                {
                    highlighted |= cue.controls & ~before;
                }

                previous = cue;
            }
            return highlighted;
        }

        private void ApplyCue(TutorialGuide.Cue cue, bool showFeedback = true)
        {
            DisplayedControls = cue != null ? cue.controls : TutorialGuide.Controls.None;
            var highlighted = showFeedback ? HighlightedControlsAt(PlaybackTime) : TutorialGuide.Controls.None;
            foreach (var key in keys)
            {
                bool down = (DisplayedControls & key.control) != 0;
                bool lit = (highlighted & key.control) != 0;
                key.face.anchoredPosition = key.position + (down ? Vector2.down * 6 : Vector2.zero);
                key.fill.sprite = lit ? null : ArchiveUi.Sprite(key.face.sizeDelta.x > 100 ? "button" : "keycap");
                key.fill.color = lit ? ArchiveUi.Cyan : Color.white;
                key.label.color = lit ? ArchiveUi.Ink : ArchiveUi.Paper;
            }
            if (mouse != null)
            {
                mouse.SetInput((DisplayedControls & TutorialGuide.Controls.MouseLeft) != 0,
                    (DisplayedControls & TutorialGuide.Controls.MouseRight) != 0,
                    (DisplayedControls & TutorialGuide.Controls.MouseMove) != 0, PlaybackTime,
                    (DisplayedControls & TutorialGuide.Controls.MouseMiddle) != 0);
                mouse.SetHighlight((highlighted & TutorialGuide.Controls.MouseLeft) != 0,
                    (highlighted & TutorialGuide.Controls.MouseRight) != 0,
                    (highlighted & TutorialGuide.Controls.MouseMiddle) != 0);
            }
            bool showFocus = cue != null && cue.highlight.width > 0 && cue.highlight.height > 0;
            focus.gameObject.SetActive(showFocus);
            var size = ((RectTransform)focus.parent).rect.size;
            if (showFocus)
            {
                focus.anchoredPosition = new Vector2(cue.highlight.x * size.x, -cue.highlight.y * size.y);
                focus.sizeDelta = new Vector2(cue.highlight.width * size.x, cue.highlight.height * size.y);
            }
            pointer.gameObject.SetActive(cue != null && cue.showPointer);
            if (cue != null && cue.showPointer)
            {
                pointer.anchoredPosition = new Vector2(cue.pointer.x * size.x, -cue.pointer.y * size.y);
                bool click = (cue.controls & TutorialGuide.Controls.MouseLeft) != 0;
                pointer.GetComponent<Image>().color = click ? ArchiveUi.Cyan : Color.white;
                pointerLabel.color = click ? Color.black : ArchiveUi.Cyan;
            }
        }

        private void FallBack()
        {
            failed = true;
            HasVideoFrame = false;
            if (player != null)
            {
                player.Stop();
            }

            if (picture != null)
            {
                picture.enabled = false;
            }

            ApplyCue(null, false);
        }

        private void OnDisable()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (player != null)
            {
                player.prepareCompleted -= Prepared;
                player.frameReady -= FrameReady;
                player.errorReceived -= Error;
                player.loopPointReached -= Looped;
                player.Stop();
                player.targetTexture = null;
            }
            if (picture != null)
            {
                picture.texture = null;
            }

            if (texture != null)
            {
                texture.Release();
                Destroy(texture);
                texture = null;
            }
            ApplyCue(null, false);
        }
    }

    // Draw mouse controls without font glyphs.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TutorialMouseGraphic : MaskableGraphic
    {
        private bool moving;
        private bool leftHeld, rightHeld, middleHeld, leftLit, rightLit, middleLit;
        private float motion;
        private Image body;
        private Image leftButton, rightButton, middleButton;
        private Vector2 leftPosition, rightPosition, middlePosition;
        private float artScale;
        private static Sprite[] layers;

        protected override void Awake()
        {
            base.Awake();
            layers ??= Resources.LoadAll<Sprite>("UI/TemporalArchive/mouse-layers");
            body = ArchiveUi.Paint(ArchiveUi.Rect("Mouse body", transform, new Vector2(0, 1),
                new Vector2(0, 1), Vector2.zero, Vector2.zero), Color.white);
            body.sprite = System.Array.Find(layers, sprite => sprite.name == "MouseBody");
            body.raycastTarget = false;
            leftButton = Button("Left button", "MouseLeft");
            rightButton = Button("Right button", "MouseRight");
            middleButton = Button("Middle button", "keycap");
            middleButton.sprite = ArchiveUi.Sprite("keycap");
            for (int i = 0; i < 3; i++)
            {
                ArchiveUi.Paint(ArchiveUi.Rect("Wheel groove", middleButton.transform, new Vector2(0, 1),
                                    new Vector2(0,
                                        1), new Vector2(5,
                                        -12 - i * 12), new Vector2(
                                        18,
                                        3)), ArchiveUi.Ink).raycastTarget = false;
            }

            LayoutLayers();
        }

        private Image Button(string name, string spriteName)
        {
            var rect = ArchiveUi.Rect(name, transform, new Vector2(0, 1), new Vector2(0, 1),
                Vector2.zero, Vector2.zero);
            var face = ArchiveUi.Paint(rect, Color.white);
            face.sprite = System.Array.Find(layers, sprite => sprite.name == spriteName);
            face.raycastTarget = false;
            var glow = rect.gameObject.AddComponent<Outline>();
            glow.effectColor = ArchiveUi.Cyan;
            glow.effectDistance = new Vector2(3, -3);
            glow.useGraphicAlpha = false;
            glow.enabled = false;
            // A solid cyan indicator stays visible against the textured button cap.
            var indicator = ArchiveUi.Stretch("Press indicator", rect);
            indicator.anchorMin = new Vector2(.4f, .22f);
            indicator.anchorMax = new Vector2(.6f, .72f);
            ArchiveUi.Paint(indicator, ArchiveUi.Cyan).raycastTarget = false;
            indicator.gameObject.SetActive(false);
            return face;
        }

        private void LayoutLayers()
        {
            if (body == null || body.sprite == null || leftButton == null || rightButton == null || middleButton == null)
            {
                return;
            }

            var nativeSize = body.sprite.rect.size;
            var available = rectTransform.rect.size;
            artScale = Mathf.Min(available.x / nativeSize.x, available.y / nativeSize.y);
            var origin = new Vector2((available.x - nativeSize.x * artScale) * .5f,
                -(available.y - nativeSize.y * artScale) * .5f);
            body.rectTransform.anchoredPosition = origin + new Vector2(motion, 0);
            body.rectTransform.sizeDelta = nativeSize * artScale;
            // Button beds measured in the generated chassis sprite's pixel coordinates.
            leftPosition = origin + new Vector2(26, -26) * artScale;
            rightPosition = origin + new Vector2(248, -26) * artScale;
            middlePosition = origin + new Vector2(198, -62) * artScale;
            leftButton.rectTransform.sizeDelta = rightButton.rectTransform.sizeDelta = new Vector2(164, 268) * artScale;
            // The wheel is an independent native UI layer in the chassis' central gap.
            middleButton.rectTransform.sizeDelta = new Vector2(34, 64);
            middleButton.rectTransform.localScale = Vector3.one * artScale;
            SetButton(leftButton, leftPosition, leftHeld, leftLit);
            SetButton(rightButton, rightPosition, rightHeld, rightLit);
            SetButton(middleButton, middlePosition, middleHeld, middleLit);
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            LayoutLayers();
        }

        public void SetInput(bool left, bool right, bool move, double time, bool middle = false)
        {
            moving = move;
            leftHeld = leftLit = left;
            rightHeld = rightLit = right;
            middleHeld = middleLit = middle;
            motion = move ? Mathf.Sin((float)time * 5f) * 9f : 0;
            LayoutLayers();
            SetVerticesDirty();
        }

        internal void SetHighlight(bool left, bool right, bool middle = false)
        {
            leftLit = left;
            rightLit = right;
            middleLit = middle;
            SetButton(leftButton, leftPosition, leftHeld, leftLit);
            SetButton(rightButton, rightPosition, rightHeld, rightLit);
            SetButton(middleButton, middlePosition, middleHeld, middleLit);
        }

        private void SetButton(Image button, Vector2 position, bool pressed, bool lit)
        {
            button.rectTransform.anchoredPosition = position + new Vector2(motion, pressed ? -16f * artScale : 0);
            button.color = lit ? new Color(.55f, 1f, .95f) : Color.white;
            button.GetComponent<Outline>().enabled = lit;
            button.transform.Find("Press indicator").gameObject.SetActive(lit);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            float x = r.x + motion, y = r.y;
            if (moving)
            {
                Quad(vh, new Rect(x - 36, y + 46, 24, 4), ArchiveUi.Cyan);
                Quad(vh, new Rect(x + r.width + 12, y + 46, 24, 4), ArchiveUi.Cyan);
                Triangle(vh,
                    new Vector2(x - 44, y + 48),
                    new Vector2(x - 32, y + 57),
                    new Vector2(x - 32, y + 39),
                    ArchiveUi.Cyan);
                Triangle(vh,
                    new Vector2(x + r.width + 44, y + 48),
                    new Vector2(x + r.width + 32, y + 39),
                    new Vector2(x + r.width + 32, y + 57),
                    ArchiveUi.Cyan);
            }
        }

        private static void Quad(VertexHelper vh, Rect r, Color color)
        {
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(r.xMin, r.yMin), color, Vector2.zero);
            vh.AddVert(new Vector3(r.xMin, r.yMax), color, Vector2.zero);
            vh.AddVert(new Vector3(r.xMax, r.yMax), color, Vector2.zero);
            vh.AddVert(new Vector3(r.xMax, r.yMin), color, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        private static void Triangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            int i = vh.currentVertCount;
            vh.AddVert(a, color, Vector2.zero);
            vh.AddVert(b, color, Vector2.zero);
            vh.AddVert(c, color, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
        }
    }
}
