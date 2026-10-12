using IceP.Application;
using IceP.Domain.SlotEngine;
using UnityEngine;
using UnityEngine.UI;

namespace IceP.Presentation
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PaylineView : Graphic
    {
        private ReelView[] reels;
        private SpinResult confirmed;
        private float highlightTime;
        public int WinningLineCount => confirmed?.Evaluation.Wins.WinningLines.Count ?? 0;
        public void Initialize(ReelView[] views) { reels = views; raycastTarget = false; }
        public void Clear()
        {
            confirmed = null; highlightTime = 0; canvasRenderer.SetAlpha(1);
            if (reels != null) foreach (var reel in reels) for (int row = 0; row < 3; row++)
            { reel.Cell(row).color = Color.white; reel.Cell(row).canvasRenderer.SetAlpha(1); }
            SetVerticesDirty();
        }
        private void Update()
        {
            if (confirmed == null || reels == null) return;
            highlightTime += Time.unscaledDeltaTime;
            float alpha = .82f + .18f * Mathf.Cos(highlightTime * 5f);
            canvasRenderer.SetAlpha(alpha);
            if (!confirmed.Evaluation.Scatter.BonusTriggered) return;
            for (int reel = 0; reel < 5; reel++) for (int row = 0; row < 3; row++)
                if (confirmed.Evaluation.Outcome.Grid[reel, row] == confirmed.Configuration.ScatterId)
                    reels[reel].Cell(row).canvasRenderer.SetAlpha(alpha);
        }
        public void Show(SpinResult spin)
        {
            Clear(); confirmed = spin;
            foreach (var win in spin.Evaluation.Wins.WinningLines)
                foreach (var cell in win.Coordinates) reels[cell.ReelIndex].Cell(cell.RowIndex).color = new Color(1, .86f, .4f);
            if (spin.Evaluation.Scatter.BonusTriggered)
                for (int reel = 0; reel < 5; reel++) for (int row = 0; row < 3; row++)
                    if (spin.Evaluation.Outcome.Grid[reel, row] == spin.Configuration.ScatterId)
                        reels[reel].Cell(row).color = new Color(.45f, 1, 1);
            SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); if (confirmed == null || reels == null) return;
            foreach (var win in confirmed.Evaluation.Wins.WinningLines)
                for (int i = 1; i < win.Coordinates.Count; i++) Segment(mesh, Center(win.Coordinates[i - 1]), Center(win.Coordinates[i]));
        }
        private Vector2 Center(GridCoordinate cell) => rectTransform.InverseTransformPoint(reels[cell.ReelIndex].Cell(cell.RowIndex).rectTransform.position);
        private static void Segment(VertexHelper mesh, Vector2 start, Vector2 end)
        {
            var offset = new Vector2(-(end - start).y, (end - start).x).normalized * 2f;
            int index = mesh.currentVertCount; var tint = new Color(1, .85f, .2f, .55f);
            mesh.AddVert(start - offset, tint, Vector2.zero); mesh.AddVert(start + offset, tint, Vector2.zero);
            mesh.AddVert(end + offset, tint, Vector2.zero); mesh.AddVert(end - offset, tint, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2); mesh.AddTriangle(index + 2, index + 3, index);
        }
    }
}
