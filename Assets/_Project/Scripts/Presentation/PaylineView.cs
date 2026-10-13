
using System.Collections.Generic;
using IceP.Application;
using IceP.Domain.SlotEngine;
using UnityEngine;
using UnityEngine.UI;

namespace IceP.Presentation
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PaylineView : Graphic
    {
        [Header("Line Appearance")]
        [SerializeField] private float glowWidth = 48f;
        [SerializeField] private float outlineWidth = 24f;
        [SerializeField] private float lineWidth = 15f;
        [SerializeField] private float coreWidth = 4f;

        [Range(0.1f, 1f)]
        [SerializeField] private float inactiveOpacity = 0.25f;

        [Header("Animation")]
        [SerializeField] private float revealDuration = 0.35f;
        [SerializeField] private float showAllDuration = 1.0f;
        [SerializeField] private float perLineDuration = 1.5f;
        [SerializeField] private float beamPeriod = 1.25f;
        [SerializeField] private float beamLength = 90f;

        private static readonly Color Gold =
            new Color(1f, 0.76f, 0.08f, 1f);

        private static readonly Color Core =
            new Color(1f, 0.97f, 0.78f, 1f);

        private static readonly Color Outline =
            new Color(0.16f, 0.08f, 0.01f, 0.95f);

        private ReelView[] reels;
        private SpinResult confirmed;

        private float elapsed;
        private int activeLine = -1;

        private readonly List<Vector2> path =
            new List<Vector2>(5);

        public int WinningLineCount =>
            confirmed?.Evaluation.Wins.WinningLines.Count ?? 0;

        public void Initialize(ReelView[] views)
        {
            reels = views;
            raycastTarget = false;
            SetVerticesDirty();
        }

        public void Clear()
        {
            confirmed = null;
            elapsed = 0f;
            activeLine = -1;

            canvasRenderer.SetAlpha(1f);

            if (reels != null)
            {
                foreach (var reel in reels)
                {
                    for (int row = 0; row < 3; row++)
                    {
                        var cell = reel.Cell(row);
                        cell.color = Color.white;
                        cell.canvasRenderer.SetAlpha(1f);
                    }
                }
            }

            SetVerticesDirty();
        }

        public void Show(SpinResult spin)
        {
            Clear();

            if (spin == null)
                return;

            confirmed = spin;

            UpdateSymbolColors();
            SetVerticesDirty();
        }

        private void Update()
        {
            if (confirmed == null || reels == null)
                return;

            elapsed += Time.unscaledDeltaTime;

            int count = WinningLineCount;

            if (count > 0)
            {
                int nextActive = -1;

                if (count > 1 && elapsed >= showAllDuration)
                {
                    float time = elapsed - showAllDuration;

                    nextActive =
                        Mathf.FloorToInt(
                            time / Mathf.Max(0.1f, perLineDuration)
                        ) % count;
                }

                if (nextActive != activeLine)
                {
                    activeLine = nextActive;
                    UpdateSymbolColors();
                }

                // Анимируем геометрию линии.
                SetVerticesDirty();
            }

            UpdateScatterPulse();
        }

        private void UpdateScatterPulse()
        {
            if (!confirmed.Evaluation.Scatter.BonusTriggered)
                return;

            float alpha =
                0.8f + 0.2f * Mathf.Cos(elapsed * 5f);

            for (int reel = 0; reel < 5; reel++)
            {
                for (int row = 0; row < 3; row++)
                {
                    if (confirmed.Evaluation.Outcome.Grid[reel, row]
                        != confirmed.Configuration.ScatterId)
                        continue;

                    reels[reel].Cell(row)
                        .canvasRenderer.SetAlpha(alpha);
                }
            }
        }

        private void UpdateSymbolColors()
        {
            if (confirmed == null || reels == null)
                return;

            foreach (var reel in reels)
            {
                for (int row = 0; row < 3; row++)
                {
                    var cell = reel.Cell(row);
                    cell.color = Color.white;
                    cell.canvasRenderer.SetAlpha(1f);
                }
            }

            var wins = confirmed.Evaluation.Wins.WinningLines;

            // Общая подсветка выигрышных символов.
            foreach (var win in wins)
            {
                foreach (var coordinate in win.Coordinates)
                {
                    reels[coordinate.ReelIndex]
                        .Cell(coordinate.RowIndex)
                        .color = new Color(1f, 0.86f, 0.5f);
                }
            }

            // Дополнительно выделяем текущую линию.
            if (activeLine >= 0)
            {
                foreach (var coordinate in
                    wins[activeLine].Coordinates)
                {
                    reels[coordinate.ReelIndex]
                        .Cell(coordinate.RowIndex)
                        .color = new Color(1f, 0.98f, 0.7f);
                }
            }

            // Scatter имеет приоритет над обычной подсветкой.
            if (confirmed.Evaluation.Scatter.BonusTriggered)
            {
                for (int reel = 0; reel < 5; reel++)
                {
                    for (int row = 0; row < 3; row++)
                    {
                        if (confirmed.Evaluation.Outcome.Grid[reel, row]
                            == confirmed.Configuration.ScatterId)
                        {
                            reels[reel].Cell(row).color =
                                new Color(0.45f, 1f, 1f);
                        }
                    }
                }
            }
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();

            if (confirmed == null || reels == null)
                return;

            int count = WinningLineCount;

            if (count == 0)
                return;

            float reveal = Mathf.Clamp01(
                elapsed / Mathf.Max(0.01f, revealDuration)
            );

            // Сначала рисуем неактивные линии.
            for (int i = 0; i < count; i++)
            {
                if (i == activeLine)
                    continue;

                bool highlighted = activeLine < 0;

                DrawWinningLine(
                    mesh,
                    i,
                    highlighted,
                    highlighted ? reveal : 1f
                );
            }

            // Активную рисуем последней, поверх остальных.
            if (activeLine >= 0)
            {
                DrawWinningLine(mesh, activeLine, true, 1f);
            }
        }

        private void DrawWinningLine(
            VertexHelper mesh,
            int lineIndex,
            bool highlighted,
            float reveal)
        {
            path.Clear();

            var win = confirmed.Evaluation
                .Wins.WinningLines[lineIndex];

            foreach (var coordinate in win.Coordinates)
                path.Add(Center(coordinate));

            if (path.Count < 2)
                return;

            float length = GetPathLength(path);
            float visibleLength = length * reveal;

            if (visibleLength <= 0.01f)
                return;

            float opacity = highlighted
                ? 1f
                : inactiveOpacity;

            float pulse =
                0.85f + 0.15f * Mathf.Sin(elapsed * 5f);

            // 1. Мягкий внешний ореол.
            DrawPath(
                mesh, visibleLength, glowWidth,
                new Color(
                    1f, 0.65f, 0.08f,
                    0.28f * opacity * pulse
                ),
                true
            );

            // 2. Тёмная контрастная обводка.
            DrawPath(
                mesh, visibleLength, outlineWidth,
                WithAlpha(Outline, opacity),
                false
            );

            // 3. Золотой основной слой.
            DrawPath(
                mesh, visibleLength, lineWidth,
                WithAlpha(Gold, opacity),
                false
            );

            // 4. Яркая бело-золотая сердцевина.
            DrawPath(
                mesh, visibleLength, coreWidth,
                WithAlpha(Core, opacity * pulse),
                false
            );

            // Блик запускается после завершения прорисовки.
            bool canShowBeam =
                highlighted &&
                reveal >= 1f &&
                (activeLine >= 0 || WinningLineCount == 1);

            if (canShowBeam)
            {
                float phase;

                if (activeLine >= 0)
                {
                    phase = Mathf.Repeat(
                        (elapsed - showAllDuration) /
                        Mathf.Max(0.1f, perLineDuration),
                        1f
                    );
                }
                else
                {
                    phase = Mathf.Repeat(
                        (elapsed - revealDuration) /
                        Mathf.Max(0.1f, beamPeriod),
                        1f
                    );
                }

                DrawBeam(mesh, length, phase);
            }
        }

        private Vector2 Center(GridCoordinate coordinate)
        {
            var rect = reels[coordinate.ReelIndex]
                .Cell(coordinate.RowIndex).rectTransform;

            Vector3 world = rect.TransformPoint(rect.rect.center);

            return rectTransform.InverseTransformPoint(world);
        }

        private static float GetPathLength(List<Vector2> points)
        {
            float length = 0f;

            for (int i = 1; i < points.Count; i++)
                length += Vector2.Distance(points[i - 1], points[i]);

            return length;
        }

        private static Color WithAlpha(Color color, float multiplier)
        {
            color.a *= multiplier;
            return color;
        }

        // Рисование линии с ограничением видимой длины.
        private void DrawPath(
            VertexHelper mesh,
            float visibleLength,
            float width,
            Color tint,
            bool gradient)
        {
            float traveled = 0f;
            bool started = false;

            for (int i = 1; i < path.Count; i++)
            {
                Vector2 a = path[i - 1];
                Vector2 b = path[i];

                float segmentLength = Vector2.Distance(a, b);

                if (segmentLength < 0.001f)
                    continue;

                float remaining = visibleLength - traveled;

                if (remaining <= 0f)
                    break;

                float fraction =
                    Mathf.Min(1f, remaining / segmentLength);

                Vector2 end = Vector2.Lerp(a, b, fraction);

                if (gradient)
                    AddGradientSegment(mesh, a, end, width, tint);
                else
                    AddSegment(mesh, a, end, width, tint, tint);

                if (!started)
                {
                    AddRoundCap(mesh, a, width * 0.5f, tint, gradient);
                    started = true;
                }

                AddRoundCap(
                    mesh, end, width * 0.5f, tint, gradient
                );

                traveled += segmentLength * fraction;
            }
        }

        // Обычный четырёхвершинный сегмент.
        private static void AddSegment(
            VertexHelper mesh,
            Vector2 a,
            Vector2 b,
            float width,
            Color colorA,
            Color colorB)
        {
            Vector2 direction = b - a;

            if (direction.sqrMagnitude < 0.0001f)
                return;

            Vector2 offset = new Vector2(
                -direction.y, direction.x
            ).normalized * (width * 0.5f);

            int index = mesh.currentVertCount;

            mesh.AddVert(a - offset, colorA, Vector2.zero);
            mesh.AddVert(a + offset, colorA, Vector2.zero);
            mesh.AddVert(b + offset, colorB, Vector2.zero);
            mesh.AddVert(b - offset, colorB, Vector2.zero);

            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index + 2, index + 3, index);
        }

        // Градиент прозрачности от центра к краям.
        private static void AddGradientSegment(
            VertexHelper mesh,
            Vector2 a,
            Vector2 b,
            float width,
            Color tint)
        {
            Vector2 direction = b - a;

            if (direction.sqrMagnitude < 0.0001f)
                return;

            Vector2 offset = new Vector2(
                -direction.y, direction.x
            ).normalized * (width * 0.5f);

            Color transparent = tint;
            transparent.a = 0f;

            int index = mesh.currentVertCount;

            mesh.AddVert(a - offset, transparent, Vector2.zero);
            mesh.AddVert(a, tint, Vector2.zero);
            mesh.AddVert(a + offset, transparent, Vector2.zero);

            mesh.AddVert(b - offset, transparent, Vector2.zero);
            mesh.AddVert(b, tint, Vector2.zero);
            mesh.AddVert(b + offset, transparent, Vector2.zero);

            mesh.AddTriangle(index, index + 1, index + 4);
            mesh.AddTriangle(index, index + 4, index + 3);

            mesh.AddTriangle(index + 1, index + 2, index + 5);
            mesh.AddTriangle(index + 1, index + 5, index + 4);
        }

        // Скругления концов и поворотов.
        private static void AddRoundCap(
            VertexHelper mesh,
            Vector2 center,
            float radius,
            Color tint,
            bool gradient)
        {
            const int segments = 12;

            int index = mesh.currentVertCount;

            mesh.AddVert(center, tint, Vector2.zero);

            Color edge = tint;

            if (gradient)
                edge.a = 0f;

            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;

                Vector2 position = center + new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle)
                ) * radius;

                mesh.AddVert(position, edge, Vector2.zero);

                if (i > 0)
                {
                    mesh.AddTriangle(
                        index,
                        index + i,
                        index + i + 1
                    );
                }
            }
        }

        // Движущийся световой импульс.
        private void DrawBeam(
            VertexHelper mesh,
            float pathLength,
            float phase)
        {
            float beamCenter =
                phase * (pathLength + beamLength)
                - beamLength * 0.5f;

            DrawBeamLayer(
                mesh, beamCenter, 22f,
                new Color(1f, 0.75f, 0.12f, 0.65f)
            );

            DrawBeamLayer(
                mesh, beamCenter, 7f,
                new Color(1f, 1f, 0.9f, 1f)
            );
        }

        private void DrawBeamLayer(
            VertexHelper mesh,
            float center,
            float width,
            Color color)
        {
            float halfLength = Mathf.Max(1f, beamLength * 0.5f);

            float beamStart = center - halfLength;
            float beamEnd = center + halfLength;

            float distance = 0f;

            for (int i = 1; i < path.Count; i++)
            {
                Vector2 a = path[i - 1];
                Vector2 b = path[i];

                float segmentLength = Vector2.Distance(a, b);

                if (segmentLength < 0.001f)
                    continue;

                float start = Mathf.Max(distance, beamStart);
                float end = Mathf.Min(
                    distance + segmentLength, beamEnd
                );

                if (end > start)
                {
                    // Разделяем отрезок в центре блика,
                    // чтобы там гарантированно была максимальная яркость.
                    if (center > start && center < end)
                    {
                        DrawBeamPiece(
                            mesh, a, b, segmentLength, distance,
                            start, center, center,
                            halfLength, width, color
                        );

                        DrawBeamPiece(
                            mesh, a, b, segmentLength, distance,
                            center, end, center,
                            halfLength, width, color
                        );
                    }
                    else
                    {
                        DrawBeamPiece(
                            mesh, a, b, segmentLength, distance,
                            start, end, center,
                            halfLength, width, color
                        );
                    }
                }

                distance += segmentLength;
            }
        }

        private static void DrawBeamPiece(
            VertexHelper mesh,
            Vector2 a,
            Vector2 b,
            float segmentLength,
            float segmentStart,
            float from,
            float to,
            float center,
            float halfLength,
            float width,
            Color tint)
        {
            Vector2 p0 = Vector2.Lerp(
                a, b, (from - segmentStart) / segmentLength
            );

            Vector2 p1 = Vector2.Lerp(
                a, b, (to - segmentStart) / segmentLength
            );

            float alpha0 = Mathf.Clamp01(
                1f - Mathf.Abs(from - center) / halfLength
            );

            float alpha1 = Mathf.Clamp01(
                1f - Mathf.Abs(to - center) / halfLength
            );

            alpha0 = Mathf.SmoothStep(0f, 1f, alpha0);
            alpha1 = Mathf.SmoothStep(0f, 1f, alpha1);

            Color color0 = tint;
            Color color1 = tint;

            color0.a *= alpha0;
            color1.a *= alpha1;

            AddSegment(
                mesh, p0, p1, width, color0, color1
            );
        }
    }
}
