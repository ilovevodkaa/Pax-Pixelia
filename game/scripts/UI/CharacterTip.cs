using System;
using PaxPixelia.Sim;

namespace PaxPixelia.UI;

/// <summary>The character of a people in a tooltip: the portrait line, every scale that leans somewhere (with the
/// bonus it gives), the permanent traits, and how it is made.</summary>
public static class CharacterTip
{
    /// <summary>Scales closer to the middle than this read as «not yet» and are left out.</summary>
    const int Shown = 10;

    public static TipCard Fill(TipCard t, NationState nat)
    {
        t.Line(Character.Portrait(nat));
        for (int k = 0; k < Character.Count; k++)
        {
            int v = Character.Value(nat, k), lvl = Character.Level(nat, k);
            if (Math.Abs(v) < Shown && lvl == 0) continue;
            var pole = Character.Pole(k, lvl != 0 ? lvl : v);
            string adj = char.ToUpperInvariant(pole.Adj[0]) + pole.Adj[1..];
            string word = Math.Abs(lvl) == 2 ? "суть" : lvl != 0 ? "склонность" : "намечается";
            t.Kv(adj, $"{word} · {Math.Abs(v)}", Math.Abs(lvl) == 2 ? Pal.Hi : lvl != 0 ? Pal.Ok : Pal.Mu);
            if (lvl != 0) t.Mu(Math.Abs(lvl) == 2 ? pole.Essence : pole.Leaning);
        }
        for (int k = 0; k < Character.Count; k++)
            for (int side = 0; side < 2; side++)
                if (Character.Hardened(nat, k, side == 1))
                {
                    var pole = side == 1 ? Character.Scales[k].Right : Character.Scales[k].Left;
                    t.Kv("Навсегда", pole.Adj, Pal.Hi).Mu(pole.Leaning);
                }
        for (int i = 0; i < Character.Traits.Length; i++)
            if (Character.Has(nat, i)) t.Kv("Черта", Character.Traits[i].Name, Pal.Hi).Mu(Character.Traits[i].Effect);
        for (int f = 0; f < Firsts.Count; f++)
            if (Firsts.Has(nat, f)) t.Kv("Первенство", Firsts.All[f].Name, Pal.Hi).Mu(Firsts.All[f].Effect);
        return t.Mu("Характер складывается из дел: фермы, рынки, святилища, открытия, разведка. Старые дела забываются примерно за 20 минут");
    }
}
