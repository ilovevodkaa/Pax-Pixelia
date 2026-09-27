using System.Collections.Generic;
using Godot;

namespace PaxPixelia.Map;

/// <summary>
/// Screen rects of the floating UI cards (outermost visible Controls that stop the mouse), so sea names can keep clear
/// of them like the mockup's uiRects(). Found generically by walking the scene tree — no dependency on UI classes —
/// and refreshed a couple of times per second.
/// </summary>
internal sealed class UiBlockers
{
    public readonly List<Rect2> Rects = new();
    ulong _next;

    public void Refresh(Node root)
    {
        ulong now = Time.GetTicksMsec();
        if (now < _next) return;
        _next = now + 500;
        Rects.Clear();
        Walk(root);
    }

    void Walk(Node n)
    {
        if (n is Control c)
        {
            if (!c.IsVisibleInTree()) return;
            if (c.MouseFilter == Control.MouseFilterEnum.Stop && c.Size.X >= 24 && c.Size.Y >= 24) { Rects.Add(c.GetGlobalRect()); return; }
        }
        else if (n is CanvasItem ci && !ci.IsVisibleInTree()) return;
        for (int i = 0, k = n.GetChildCount(); i < k; i++) Walk(n.GetChild(i));
    }

    public bool Hits(Rect2 r, float margin)
    {
        var g = r.Grow(margin);
        foreach (var u in Rects) if (u.Intersects(g)) return true;
        return false;
    }
}
