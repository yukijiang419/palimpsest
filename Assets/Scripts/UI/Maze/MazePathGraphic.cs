using System.Collections.Generic;
using Caffeine.Gameplay.Maze;
using UnityEngine;
using UnityEngine.UI;

namespace Caffeine.Gameplay.UI
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MazePathGraphic : MaskableGraphic
    {
        private MazeBoardSession session;
        private float cellSize, spacing, padding;

        public void SetPath(MazeBoardSession value, float size, float gap, float inset)
        {
            session = value; cellSize = size; spacing = gap; padding = inset;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (session == null) return;
            Rect rect = GetPixelAdjustedRect();
            Vector2 Center(GridPosition p) => new(rect.xMin + padding + cellSize / 2 + p.X * (cellSize + spacing),
                rect.yMax - padding - cellSize / 2 - (session.Board.Height - 1 - p.Y) * (cellSize + spacing));
            var vertices = new List<UIVertex>();
            var triangles = new List<int>();
            for (int i = 1; i < session.Path.Count; i++)
            {
                Vector2 from = Center(session.Path[i - 1]), to = Center(session.Path[i]);
                Vector2 direction = (to - from).normalized;
                // Keep the centre of each effect symbol visible.
                PolygonTokenGeometry.AddEdgeQuad(vertices, triangles, from + direction * cellSize * .26f,
                    to - direction * cellSize * .26f, 2, ArchiveUi.Cyan);
            }
            Vector2 cursor = Center(session.Cursor);
            var cursorRect = new Rect(cursor - Vector2.one * (cellSize * .5f - 2), Vector2.one * (cellSize - 4));
            Vector2[] corners = { new(cursorRect.xMin, cursorRect.yMin), new(cursorRect.xMin, cursorRect.yMax),
                new(cursorRect.xMax, cursorRect.yMax), new(cursorRect.xMax, cursorRect.yMin) };
            for (int i = 0; i < 4; i++)
                PolygonTokenGeometry.AddEdgeQuad(vertices, triangles, corners[i], corners[(i + 1) % 4], 2, ArchiveUi.Paper);
            vh.AddUIVertexStream(vertices, triangles);
        }
    }
}
