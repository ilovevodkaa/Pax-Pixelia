using System.Collections.Generic;

namespace PaxPixelia.Map;

/// <summary>Accumulates the province lists of Game change events until the next frame (null list = everything).</summary>
internal sealed class ChangeSet
{
    public readonly List<int> List = new();
    public bool All { get; private set; }
    public bool Any { get; private set; }

    public void Add(IReadOnlyList<int> ps)
    {
        Any = true;
        if (ps == null) { All = true; List.Clear(); }
        else if (!All) foreach (int p in ps) List.Add(p);
    }

    public void Clear() { List.Clear(); All = Any = false; }
}
