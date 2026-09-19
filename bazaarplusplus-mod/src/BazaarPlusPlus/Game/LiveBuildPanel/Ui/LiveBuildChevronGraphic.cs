#nullable enable
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.LiveBuildPanel.Ui;

internal sealed class LiveBuildChevronGraphic : MaskableGraphic
{
    public override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var rect = GetPixelAdjustedRect();
        // One closed contour with shared vertices through the elbow.
        Vertex(mesh, rect, 0, .875f);
        Vertex(mesh, rect, .2f, 1);
        Vertex(mesh, rect, 1, .5f);
        Vertex(mesh, rect, .2f, 0);
        Vertex(mesh, rect, 0, .125f);
        Vertex(mesh, rect, .6f, .5f);
        mesh.AddTriangle(0, 1, 5);
        mesh.AddTriangle(1, 2, 5);
        mesh.AddTriangle(2, 3, 5);
        mesh.AddTriangle(3, 4, 5);
    }

    private void Vertex(VertexHelper mesh, Rect rect, float x, float y)
    {
        var vertex = UIVertex.simpleVert;
        vertex.position = new Vector2(rect.xMin + rect.width * x, rect.yMin + rect.height * y);
        vertex.color = color;
        mesh.AddVert(vertex);
    }
}
