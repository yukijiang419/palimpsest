using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Caffeine.Gameplay.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasRenderer))]
    // draw a polygon whose border and fill show the outcome progress
    public sealed class PolygonTokenGraphic : MaskableGraphic
    {
        private int sideCount = 3;
        private bool collectionMode;
        private bool collected;
        private int stageOneThreshold = 3;
        private int stageTwoThreshold = 5;
        private int progress = -1;
        private float outlineThickness = 2f;
        private Color tokenColor = Color.white;
        private Color neutralColor = Color.gray;
        private float pulseTime;
        private Image symbol;
        public int Progress => progress;
        public int Stage => progress >= stageTwoThreshold ? 2 : progress >= stageOneThreshold ? 1 : 0;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        public void ConfigureStyle(
            int sides,
            int stageOne,
            int stageTwo,
            Color activeColor,
            Color inactiveColor,
            float thickness)
        {
            sideCount = sides;
            stageOneThreshold = stageOne;
            stageTwoThreshold = stageTwo;
            tokenColor = activeColor;
            neutralColor = inactiveColor;
            outlineThickness = thickness;
            var symbolRect = transform.Find("Archive symbol") as RectTransform;
            if (symbolRect == null)
            {
                symbolRect = ArchiveUi.Stretch("Archive symbol", transform);
                symbolRect.anchorMin = Vector2.one * .09f;
                symbolRect.anchorMax = Vector2.one * .91f;
            }
            symbol = ArchiveUi.Surface(symbolRect, sides == 3 ? "node-heal" : sides == 4 ? "node-break" : "node-bind");
            symbol.preserveAspect = true;
            UpdateSymbol();
            SetVerticesDirty();
        }

        public void SetCollected(bool value)
        {
            if (collectionMode && collected == value) return;
            collectionMode = true;
            collected = value;
            UpdateSymbol();
            SetVerticesDirty();
        }

        public void SetProgress(int value)
        {
            // rebuild the mesh only when the displayed count changes
            if (!collectionMode && progress == value)
            {
                return;
            }

            collectionMode = false;
            progress = value;
            UpdateSymbol();
            SetVerticesDirty();
        }

        private void UpdateSymbol()
        {
            if (symbol != null) symbol.color = Color.white * (collectionMode ? collected ? 1f : .68f : Stage > 0 ? 1f : .68f);
        }

        public void AnimateCharge(float deltaTime)
        {
            if (collectionMode || Stage == 0 || deltaTime <= 0f)
            {
                return;
            }

            pulseTime += deltaTime;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            Rect rect = GetPixelAdjustedRect();
            Vector2[] polygon = PolygonTokenGeometry.BuildVertices(rect, sideCount);
            var vertices = new List<UIVertex>();
            var triangles = new List<int>();

            if (collectionMode)
            {
                for (int edge = 0; edge < sideCount; edge++)
                    PolygonTokenGeometry.AddEdgeQuad(vertices, triangles, polygon[edge],
                        polygon[(edge + 1) % sideCount], outlineThickness, tokenColor);
                if (collected && symbol == null)
                    PolygonTokenGeometry.AddClockwiseFill(vertices, triangles, rect.center, polygon, 1f, tokenColor);
                vertexHelper.AddUIVertexStream(vertices, triangles);
                return;
            }

            // draw the neutral border underneath the progress color
            for (int edge = 0; edge < sideCount; edge++)
            {
                PolygonTokenGeometry.AddEdgeQuad(
                    vertices,
                    triangles,
                    polygon[edge],
                    polygon[(edge + 1) % sideCount],
                    outlineThickness,
                    neutralColor);
            }

            // fill the border as progress approaches stage one
            float filledPerimeter = Mathf.Clamp01(progress / (float)stageOneThreshold) * sideCount;
            int filledEdges = Mathf.FloorToInt(filledPerimeter);
            for (int edge = 0; edge < filledEdges && edge < sideCount; edge++)
            {
                PolygonTokenGeometry.AddEdgeQuad(
                    vertices,
                    triangles,
                    polygon[edge],
                    polygon[(edge + 1) % sideCount],
                    outlineThickness,
                    tokenColor);
            }

            // draw the unfinished part of the next border edge
            if (filledEdges < sideCount)
            {
                float partialEdge = filledPerimeter - filledEdges;
                if (partialEdge > 0f)
                {
                    Vector2 from = polygon[filledEdges];
                    Vector2 to = Vector2.Lerp(from, polygon[(filledEdges + 1) % sideCount], partialEdge);
                    PolygonTokenGeometry.AddEdgeQuad(
                        vertices,
                        triangles,
                        from,
                        to,
                        outlineThickness,
                        tokenColor);
                }
            }

            // fully charge the core at the first threshold because it already triggers combat
            if (Stage > 0)
            {
                Color fill = tokenColor;
                fill.a *= 0.78f + 0.15f * Mathf.Sin(pulseTime * 4f);
                if (symbol == null) PolygonTokenGeometry.AddClockwiseFill(vertices, triangles, rect.center, polygon, 1f, fill);
                if (Stage == 2)
                {
                    Color rim = Color.Lerp(tokenColor, Color.white, 0.65f);
                    for (int edge = 0; edge < sideCount; edge++)
                    {
                        PolygonTokenGeometry.AddEdgeQuad(vertices, triangles, polygon[edge],
                                                    polygon[(edge + 1) % sideCount], outlineThickness + 1f, rim);
                    }
                }
            }

            vertexHelper.AddUIVertexStream(vertices, triangles);
        }
    }
}
