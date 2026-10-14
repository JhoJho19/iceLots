using UnityEngine;
using UnityEngine.UI;

namespace IceP.Presentation
{
    // One generated quad outside the glyph mesh. The shader draws its flare without a texture.
    [AddComponentMenu("")]
    public sealed class TmpGoldSparkleGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = rectTransform.rect;
            var vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = new Vector3(rect.xMin, rect.yMin); vertex.uv0 = new Vector4(0, 0, 0, 1); mesh.AddVert(vertex);
            vertex.position = new Vector3(rect.xMin, rect.yMax); vertex.uv0 = new Vector4(0, 1, 0, 1); mesh.AddVert(vertex);
            vertex.position = new Vector3(rect.xMax, rect.yMax); vertex.uv0 = new Vector4(1, 1, 0, 1); mesh.AddVert(vertex);
            vertex.position = new Vector3(rect.xMax, rect.yMin); vertex.uv0 = new Vector4(1, 0, 0, 1); mesh.AddVert(vertex);
            mesh.AddTriangle(0, 1, 2); mesh.AddTriangle(2, 3, 0);
        }
    }
}
