using System.Collections.Generic;
using Caffeine.Gameplay.Config;
using Caffeine.Gameplay.Enemy;
using Caffeine.Gameplay.Maze;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Caffeine.Gameplay.UI
{
    // keep released particles visible after the maze hides its source icons
    [DefaultExecutionOrder(200)]
    public sealed class MazeOutcomeParticleLayer : MonoBehaviour
    {
        internal const float GatherDuration = 0.1f;
        internal const float FlightDuration = 0.4f;
        private const float Stagger = 0.06f;
        private const float LostTargetFade = 0.14f;

        private sealed class Burst
        {
            public bool Active, Lost, HasArrived;
            public MazeOutcome Effect;
            public EnemyController Target;
            public EnemyHealthBar EnemyBar;
            public PlayerHudPresenter Hud;
            public Color Color;
            public Vector2 Origin, Position, Destination;
            public Rect Bar;
            public float Age, LostAge;
            public int TrailCount, TrailHead;
            public readonly Vector2[] Trail = new Vector2[12];

            public void Clear()
            {
                Active = false;
                Target = null;
                EnemyBar = null;
                Hud = null;
                TrailCount = TrailHead = 0;
            }
        }

        private readonly List<Burst> pool = new List<Burst>(24);
        private RectTransform surface;
        private MazeOutcomeParticleGraphic graphic;
        private GameTimeController timeController;
        private Color[] colors;
        private readonly Vector3[] corners = new Vector3[4];

        public int ActiveBurstCount
        {
            get
            {
                int count = 0;
                foreach (var burst in pool)
                {
                    if (burst.Active)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public static MazeOutcomeParticleLayer Create(Canvas source, GameTimeController clock,
            GameplayConfig.UiSettings settings)
        {
            var root = new GameObject("Maze outcome particles",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            if (source != null)
            {
                SceneManager.MoveGameObjectToScene(root, source.gameObject.scene);
            }

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = source != null ? source.rootCanvas.sortingOrder + 20 : 20;
            var scaler = root.GetComponent<CanvasScaler>();
            var original = source != null ? source.rootCanvas.GetComponent<CanvasScaler>() : null;
            if (original != null)
            {
                scaler.uiScaleMode = original.uiScaleMode;
                scaler.referenceResolution = original.referenceResolution;
                scaler.screenMatchMode = original.screenMatchMode;
                scaler.matchWidthOrHeight = original.matchWidthOrHeight;
                scaler.scaleFactor = original.scaleFactor;
                scaler.referencePixelsPerUnit = original.referencePixelsPerUnit;
            }
            var layer = root.AddComponent<MazeOutcomeParticleLayer>();
            layer.timeController = clock;
            layer.colors = new[] { settings.RecoveryColor, settings.DamageColor, settings.ControlColor };
            layer.surface = ArchiveUi.Stretch("Effect mesh", root.transform);
            layer.graphic = layer.surface.gameObject.AddComponent<MazeOutcomeParticleGraphic>();
            layer.graphic.Layer = layer;
            for (int i = 0; i < 12; i++)
            {
                layer.pool.Add(new Burst());
            }

            return layer;
        }

        private void OnEnable() => SceneManager.activeSceneChanged += HandleSceneChanged;

        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= HandleSceneChanged;
            Clear();
        }

        private void HandleSceneChanged(Scene previous, Scene next) => Clear();

        private void Clear()
        {
            foreach (var burst in pool)
            {
                burst.Clear();
            }

            if (graphic != null)
            {
                graphic.SetVerticesDirty();
            }
        }

        public void Release(MazeResolution resolution, PolygonTokenGraphic[] sources)
        {
            if (resolution == null || sources == null)
            {
                return;
            }

            int order = 0;
            foreach (MazeOutcome effect in resolution.Effects)
            {
                int kind = (int)effect.Type;
                if (kind < 0 || kind >= sources.Length || sources[kind] == null)
                {
                    continue;
                }

                Burst burst = pool.Find(item => !item.Active);
                if (burst == null)
                {
                    burst = new Burst();
                    pool.Add(burst);
                }
                burst.Active = true;
                burst.Lost = burst.HasArrived = false;
                burst.Effect = effect;
                burst.Target = resolution.Target;
                burst.EnemyBar = resolution.Target != null
                    ? resolution.Target.GetComponentInChildren<EnemyHealthBar>(true) : null;
                burst.Hud = null;
                if (effect.Type == OutcomeType.Recovery)
                {
                    foreach (var hud in FindObjectsByType<PlayerHudPresenter>(FindObjectsSortMode.None))
                    {
                        if (hud.EffectOwner == resolution.Player)
                        {
                            burst.Hud = hud;
                            break;
                        }
                    }
                }

                var source = sources[kind].rectTransform;
                Canvas sourceCanvas = sources[kind].canvas;
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(
                    sourceCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : sourceCanvas.worldCamera,
                    source.TransformPoint(source.rect.center));
                burst.Origin = burst.Position = ToLocal(screen);
                burst.Color = colors[kind];
                burst.Age = -order++ * Stagger;
                burst.LostAge = 0;
                burst.TrailCount = burst.TrailHead = 0;
                if (!Locate(burst))
                {
                    burst.Lost = true;
                }
            }
            graphic.SetVerticesDirty();
        }

        private Vector2 ToLocal(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(surface, screen, null, out Vector2 local);
            return local;
        }

        private bool Locate(Burst burst)
        {
            Rect screenRect;
            if (burst.Effect.Type == OutcomeType.Recovery)
            {
                if (burst.Hud == null || !burst.Hud.isActiveAndEnabled || burst.Hud.HealthEffectAnchor == null
                    || !burst.Hud.HealthEffectAnchor.gameObject.activeInHierarchy)
                {
                    return false;
                }

                var anchor = burst.Hud.HealthEffectAnchor;
                anchor.GetWorldCorners(corners);
                Canvas canvas = anchor.GetComponentInParent<Canvas>();
                Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                screenRect = Rect.MinMaxRect(a.x, a.y, b.x, b.y);
            }
            else
            {
                if (burst.Target == null || !burst.Target.IsAlive || !burst.Target.isActiveAndEnabled
                    || burst.EnemyBar == null || !burst.EnemyBar.TryGetEffectScreenRect(
                        burst.Effect.Type == OutcomeType.Damage && burst.Effect.ShieldLoss > 0f, out screenRect))
                {
                    return false;
                }
            }
            Vector2 min = ToLocal(screenRect.min), max = ToLocal(screenRect.max);
            burst.Bar = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            burst.Destination = burst.Bar.center;
            if (burst.Effect.Type == OutcomeType.Recovery && burst.Effect.HealthRestored > 0f)
            {
                burst.Destination.x = Mathf.Lerp(burst.Bar.xMin, burst.Bar.xMax,
                                    (burst.Effect.HealthBefore + burst.Effect.HealthAfter) * 0.5f / burst.Effect.MaxHealth);
            }

            return true;
        }

        private void LateUpdate()
        {
            if (graphic == null || timeController != null && timeController.IsPaused)
            {
                return;
            }

            float delta = Time.unscaledDeltaTime;
            bool dirty = false;
            foreach (var burst in pool)
            {
                if (!burst.Active)
                {
                    continue;
                }

                dirty = true;
                burst.Age += delta;
                if (!burst.Lost && !Locate(burst))
                {
                    burst.Lost = true;
                }

                if (burst.Lost)
                {
                    burst.LostAge += delta;
                    if (burst.LostAge >= LostTargetFade)
                    {
                        burst.Clear();
                    }

                    continue;
                }
                if (burst.Age < GatherDuration)
                {
                    continue;
                }

                float flight = Mathf.Clamp01((burst.Age - GatherDuration) / FlightDuration);
                burst.Position = Arc(burst, flight);
                burst.Trail[burst.TrailHead] = burst.Position;
                burst.TrailHead = (burst.TrailHead + 1) % burst.Trail.Length;
                burst.TrailCount = Mathf.Min(burst.TrailCount + 1, burst.Trail.Length);
                burst.HasArrived = flight >= 1f;
                if (burst.HasArrived)
                {
                    float impactAge = burst.Age - GatherDuration - FlightDuration;
                    bool status = HasStatus(burst);
                    if (impactAge > 0.55f && !status)
                    {
                        burst.Clear();
                    }
                }
            }
            if (dirty)
            {
                graphic.SetVerticesDirty();
            }
        }

        private static bool HasStatus(Burst burst)
        {
            if (burst.Effect.Type == OutcomeType.Control)
            {
                return burst.Target != null && burst.Target.IsControlled
                                    && burst.Effect.ControlEndsAt > Time.time;
            }

            return burst.Effect.Type == OutcomeType.Recovery && burst.Hud != null
                            && burst.Hud.EffectOwner != null && burst.Hud.EffectOwner.DamageReduction > 0f
                            && burst.Effect.ReductionEndsAt > Time.time;
        }

        private static Vector2 Arc(Burst burst, float t)
        {
            float eased = t * t * (3f - 2f * t);
            Vector2 point = Vector2.Lerp(burst.Origin, burst.Destination, eased);
            float bend = Mathf.Clamp(Vector2.Distance(burst.Origin, burst.Destination) * 0.2f, 50f, 180f);
            point += Vector2.up * (4f * t * (1f - t) * bend);
            return point;
        }

        internal void Draw(VertexHelper mesh)
        {
            foreach (var burst in pool)
            {
                if (!burst.Active || burst.Age < 0)
                {
                    continue;
                }

                Color tint = burst.Color;
                if (burst.Effect.Stage == 2)
                {
                    tint = Color.Lerp(tint, Color.white, 0.18f);
                }

                float alpha = burst.Lost ? Mathf.Clamp01(1f - burst.LostAge / LostTargetFade) : 1f;
                if (!burst.HasArrived || burst.Lost)
                {
                    DrawFlight(mesh, burst, tint, alpha);
                }
                else
                {
                    DrawArrival(mesh, burst, tint);
                }
            }
        }

        private static Color Fade(Color color, float alpha)
        {
            color.a *= Mathf.Clamp01(alpha);
            return color;
        }

        private static void DrawFlight(VertexHelper mesh, Burst burst, Color tint, float alpha)
        {
            bool gathering = burst.Age < GatherDuration;
            Vector2 center = gathering ? burst.Origin : burst.Position;
            float gather = Mathf.Clamp01(burst.Age / GatherDuration);
            float radius = burst.Effect.Stage == 2 ? 12f : 10f;
            if (gathering)
            {
                radius *= 1f + gather * 0.25f;
            }

            for (int i = 1; i < burst.TrailCount; i++)
            {
                int a = (burst.TrailHead - burst.TrailCount + i - 1 + burst.Trail.Length) % burst.Trail.Length;
                int b = (a + 1) % burst.Trail.Length;
                float fade = i / (float)burst.TrailCount;
                Line(mesh, burst.Trail[a], burst.Trail[b], (2f + fade * 4f), Fade(tint, fade * alpha * 0.65f));
            }
            Polygon(mesh, center, radius * 1.7f, 12, Fade(tint, 0.08f * alpha));
            Polygon(mesh, center, radius, (int)burst.Effect.Type + 3, Fade(tint, alpha));
            Polygon(mesh, center, radius * 0.44f, (int)burst.Effect.Type + 3,
                Fade(Color.Lerp(tint, Color.white, 0.7f), alpha));
            int fragments = burst.Effect.Stage == 2 ? 12 : 7;
            for (int i = 0; i < fragments; i++)
            {
                float angle = i * 2.39996f + burst.Age * 3f;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float distance = gathering ? Mathf.Lerp(32f, 13f, gather) : 15f + i % 3 * 5f;
                Vector2 point = center + direction * distance;
                Polygon(mesh, point, i % 2 == 0 ? 2.8f : 1.8f, (int)burst.Effect.Type + 3, Fade(tint, alpha * 0.8f));
            }
        }

        private static void DrawArrival(VertexHelper mesh, Burst burst, Color tint)
        {
            float age = burst.Age - GatherDuration - FlightDuration;
            float flash = Mathf.Clamp01(1f - age / 0.55f);
            Rect bar = burst.Bar;
            Vector2 center = burst.Destination;
            if (burst.Effect.Type == OutcomeType.Recovery)
            {
                float absorb = Mathf.Clamp01(1f - age / 0.18f);
                if (absorb > 0f)
                {
                    for (int i = 0; i < (burst.Effect.Stage == 2 ? 10 : 6); i++)
                    {
                        float angle = i * 2.39996f;
                        Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 22f * absorb;
                        Polygon(mesh, center + offset, 2.5f * absorb, 3, Fade(tint, absorb));
                    }
                }

                if (burst.Effect.HealthRestored > 0f)
                {
                    Rect healed = bar;
                    healed.xMin = Mathf.Lerp(bar.xMin, bar.xMax, burst.Effect.HealthBefore / burst.Effect.MaxHealth);
                    healed.xMax = Mathf.Lerp(bar.xMin, bar.xMax, burst.Effect.HealthAfter / burst.Effect.MaxHealth);
                    FillRect(mesh, healed, Fade(tint, flash * 0.8f));
                    Ring(mesh, center, 5f + age * 25f, 2f, Fade(tint, flash));
                }
                if (HasStatus(burst))
                {
                    float remaining = Mathf.Clamp01((burst.Effect.ReductionEndsAt - Time.time) / 0.4f);
                    Outline(mesh, Expand(bar, 3f), 2f, Fade(tint, remaining * 0.8f));
                }
            }
            else if (burst.Effect.Type == OutcomeType.Damage)
            {
                Outline(mesh, Expand(bar, 2f), 2.5f, Fade(tint, flash));
                Ring(mesh, center, 4f + age * 50f, 2.5f, Fade(tint, flash));
                int shards = burst.Effect.Stage == 2 ? 12 : 8;
                if (burst.Effect.ShieldBroken)
                {
                    for (int i = 0; i < shards; i++)
                    {
                        float angle = i * Mathf.PI * 2f / shards;
                        Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                        Vector2 point = center + direction * (10f + age * 80f);
                        Line(mesh, point, point + direction * 7f, 2.5f, Fade(tint, flash));
                    }
                }
                else
                {
                    Polygon(mesh, center, 16f * flash, 4, Fade(Color.white, flash * 0.8f));
                }
            }
            else if (HasStatus(burst))
            {
                float remaining = Mathf.Clamp01((burst.Effect.ControlEndsAt - Time.time) / 0.4f);
                float tighten = 1f - Mathf.Clamp01(age / 0.18f);
                Rect bounds = Expand(bar, 8f + tighten * 12f);
                Chain(mesh,
                    new Vector2(bounds.xMin, bounds.yMin),
                    new Vector2(bounds.xMax, bounds.yMax),
                    Fade(tint, remaining));
                Chain(mesh,
                    new Vector2(bounds.xMin, bounds.yMax),
                    new Vector2(bounds.xMax, bounds.yMin),
                    Fade(tint, remaining));
                Polygon(mesh, bar.center, 4f, 5, Fade(Color.Lerp(tint, Color.white, 0.4f), remaining));
            }
        }

        private static Rect Expand(Rect rect, float padding) => Rect.MinMaxRect(
            rect.xMin - padding, rect.yMin - padding, rect.xMax + padding, rect.yMax + padding);

        private static void Chain(VertexHelper mesh, Vector2 a, Vector2 b, Color color)
        {
            int links = Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(a, b) / 11f), 3, 40);
            Vector2 step = (b - a) / links;
            Vector2 normal = new Vector2(-step.y, step.x).normalized * 3f;
            for (int i = 0; i < links; i++)
            {
                Vector2 start = a + step * i, end = start + step;
                Line(mesh, start, (start + end) * 0.5f + normal, 1.7f, color);
                Line(mesh, (start + end) * 0.5f + normal, end, 1.7f, color);
                Line(mesh, start, (start + end) * 0.5f - normal, 1.7f, color);
                Line(mesh, (start + end) * 0.5f - normal, end, 1.7f, color);
            }
        }

        private static void Outline(VertexHelper mesh, Rect rect, float width, Color color)
        {
            Vector2 a = rect.min, b = new Vector2(rect.xMin,
                rect.yMax), c = rect.max, d = new Vector2(
                rect.xMax,
                rect.yMin);
            Line(mesh, a, b, width, color);
            Line(mesh, b, c, width, color);
            Line(mesh, c, d, width, color);
            Line(mesh, d, a, width, color);
        }

        private static void Ring(VertexHelper mesh, Vector2 center, float radius, float width, Color color)
        {
            Vector2 previous = center + Vector2.right * radius;
            for (int i = 1; i <= 20; i++)
            {
                float angle = i * Mathf.PI * 2f / 20;
                Vector2 next = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                Line(mesh, previous, next, width, color);
                previous = next;
            }
        }

        private static void FillRect(VertexHelper mesh, Rect rect, Color color)
        {
            if (rect.width <= 0f || color.a <= 0f)
            {
                return;
            }

            int index = mesh.currentVertCount;
            mesh.AddVert(rect.min, color, Vector2.zero);
            mesh.AddVert(new Vector2(rect.xMin, rect.yMax), color, Vector2.zero);
            mesh.AddVert(rect.max, color, Vector2.zero);
            mesh.AddVert(new Vector2(rect.xMax, rect.yMin), color, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
        }

        private static void Line(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color color)
        {
            if (color.a <= 0f)
            {
                return;
            }

            Vector2 offset = new Vector2(-(b - a).y, (b - a).x).normalized * width * 0.5f;
            int index = mesh.currentVertCount;
            mesh.AddVert(a - offset, color, Vector2.zero);
            mesh.AddVert(a + offset, color, Vector2.zero);
            mesh.AddVert(b + offset, color, Vector2.zero);
            mesh.AddVert(b - offset, color, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
        }

        private static void Polygon(VertexHelper mesh, Vector2 center, float radius, int sides, Color color)
        {
            if (color.a <= 0f || radius <= 0f)
            {
                return;
            }

            int index = mesh.currentVertCount;
            mesh.AddVert(center, color, Vector2.zero);
            for (int i = 0; i < sides; i++)
            {
                float angle = (90f - i * 360f / sides) * Mathf.Deg2Rad;
                mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color, Vector2.zero);
            }
            for (int i = 0; i < sides; i++)
            {
                mesh.AddTriangle(index, index + 1 + i, index + 1 + (i + 1) % sides);
            }
        }
    }
}
