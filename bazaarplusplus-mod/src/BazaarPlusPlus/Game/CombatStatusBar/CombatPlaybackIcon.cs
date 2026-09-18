#nullable enable
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.CombatStatusBar;

internal sealed class CombatPlaybackIcon : MaskableGraphic
{
    internal bool ShowPlay { get; set; }

    public override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var rect = GetPixelAdjustedRect();
        if (ShowPlay)
        {
            Vertex(mesh, rect.xMin, rect.yMin);
            Vertex(mesh, rect.xMin, rect.yMax);
            Vertex(mesh, rect.xMax, rect.center.y);
            mesh.AddTriangle(0, 1, 2);
            return;
        }

        Bar(mesh, rect.xMin, rect.xMin + rect.width * 0.3f, rect);
        Bar(mesh, rect.xMax - rect.width * 0.3f, rect.xMax, rect);
    }

    private void Bar(VertexHelper mesh, float left, float right, Rect rect)
    {
        int start = mesh.currentVertCount;
        Vertex(mesh, left, rect.yMin);
        Vertex(mesh, left, rect.yMax);
        Vertex(mesh, right, rect.yMax);
        Vertex(mesh, right, rect.yMin);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start, start + 2, start + 3);
    }

    private void Vertex(VertexHelper mesh, float x, float y)
    {
        var vertex = UIVertex.simpleVert;
        vertex.position = new Vector2(x, y);
        vertex.color = color;
        mesh.AddVert(vertex);
    }
}
