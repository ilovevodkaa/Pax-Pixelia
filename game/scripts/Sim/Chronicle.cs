using System;
using System.Collections.Generic;
using PaxPixelia.Core;
using PaxPixelia.World;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Sim;

/// <summary>
/// Periodic events: a serious chronicle with historical jokes (GDD 9.8). Events are dealt from a deck that is
/// reshuffled every cycle from the world seed, so nothing repeats until all have been seen and every client
/// draws the same card. An event returns null when it does not apply (e.g. no foreign nation met yet).
/// </summary>
public static class Chronicle
{
    sealed class Ctx
    {
        public WorldData W; public GameState S; public int Salt;
        List<int> _own;

        /// <summary>A province of the local player, or -1.</summary>
        public int Own()
        {
            _own ??= Owned(S, GameState.LocalPlayer);
            return _own.Count == 0 ? -1 : _own[SimRng.Pick(W.Seed, 11, S.Year, Salt, _own.Count)];
        }

        public IEnumerable<int> AllOwn() => _own ??= Owned(S, GameState.LocalPlayer);

        /// <summary>A foreign nation the player has met, or -1.</summary>
        public int MetForeign()
        {
            var met = new List<int>();
            for (int n = 0; n < S.Met.Length; n++) if (n != GameState.LocalPlayer && S.Met[n]) met.Add(n);
            return met.Count == 0 ? -1 : met[SimRng.Pick(W.Seed, 12, S.Year, Salt, met.Count)];
        }

        public string Name(int p) => W.PName[p];
        public void Mood(int p, int d) => S.Mood[p] = (byte)Math.Clamp(S.Mood[p] + d, 0, 100);
        public void MoodAll(int d) { foreach (int p in AllOwn()) Mood(p, d); }
        public void Pop(int p, float k) => S.Pop[p] = Math.Max(10f, S.Pop[p] * k);
    }

    delegate (string icon, string text)? Event(Ctx c);

    static readonly string[] ReligionGen = { "Культа Солнца", "Пути Мирры", "Древних духов", "Огненного завета" };

    static readonly Event[] Deck =
    {
        c => { // Эврика
            int farms = 0;
            foreach (int p in c.AllOwn()) foreach (var b in c.S.Buildings[p]) if (b == Bld.Farm) farms++;
            return farms >= 2
                ? ("bulb", $"Эврика! {Ru.Count(farms, "ферма", "фермы", "ферм")} ускорили исследование «Ирригация» на 20%")
                : ("bulb", "Эврика! Гончар из столицы придумал круг. Жрецы спорят, не колдовство ли это");
        },
        c => { // обмен
            int n = c.MetForeign();
            c.S.Gold += 20;
            return n < 0
                ? ("scale", "Кочевые торговцы предлагают обмен: соль на бронзовые топоры. Совет согласился — казна +20")
                : ("scale", $"{Data.Nations[n].Name} предлагает обмен: камень на вино. Совет согласился — казна +20");
        },
        c => { // эму-война
            int p = c.Own(); if (p < 0) return null;
            c.Pop(p, .99f); c.Mood(p, -3);
            return ("feather", $"В провинции {c.Name(p)} эму объявили войну урожаю. Армия в замешательстве");
        },
        c => { // танцевальная чума
            int p = c.Own(); if (p < 0) return null;
            c.Mood(p, -5);
            return ("music", $"В провинции {c.Name(p)} началась танцевальная чума: жители пляшут вместо работы. Довольство −5");
        },
        c => { // затмение
            int r = Data.Nations[GameState.LocalPlayer].Religion;
            c.MoodAll(5);
            return ("sun", $"Жрецы {(r >= 0 && r < ReligionGen.Length ? ReligionGen[r] : "храма")} верно предсказали затмение. Довольство +5");
        },
        c => { // колодец
            int p = c.Own(); if (p < 0) return null;
            c.Mood(p, -3);
            return ("users", $"Общинники провинции {c.Name(p)} требуют новый колодец. Пока его нет, довольство −3");
        },
        c => { // конь-советник
            int cap = Scouts.Capital(c.S); if (cap >= 0) c.Mood(cap, -2);
            return ("crown", "Правитель назначил своего коня советником. Знать возмущена, конь невозмутим");
        },
        c => { // караван
            int n = c.MetForeign();
            c.S.Gold += 25;
            string from = n < 0 ? "дальних земель" : Ru.Genitive(Data.Nations[n].Name);
            return ("coins", $"Караван из {from} привёз соль и свежие сплетни. Казна +25");
        },
        c => { // урожай
            int p = c.Own(); if (p < 0) return null;
            c.Pop(p, 1.03f); c.Mood(p, 3);
            return ("plant", $"Урожайный год в провинции {c.Name(p)}: амбары полны, население +3%");
        },
        c => { // засуха
            int p = c.Own(); if (p < 0) return null;
            c.Pop(p, .96f); c.Mood(p, -3);
            return ("droplet-off", $"Засуха в провинции {c.Name(p)}: урожай скуден, часть семей откочевала. Население −4%");
        },
        c => { // Диоген
            int p = c.Own(); if (p < 0) return null;
            return ("user", $"Мудрец из провинции {c.Name(p)} поселился в бочке и попросил правителя не заслонять ему солнце. Правитель отошёл");
        },
        c => ("cat", "Кот правителя уснул на картах разведчиков. Будить его не решились: совет перенесён на завтра"),
        c => { // сыр
            int p = c.Own(); if (p < 0) return null;
            c.Mood(p, 3);
            return ("cheese", $"В провинции {c.Name(p)} сварили сыр размером с тележное колесо. Жрецы спорят, еда это или реликвия");
        },
        c => { // летописец
            int y = c.S.Year;
            string year = y < 0 ? $"{-y} год до н. э." : $"{y} год н. э.";
            return ("book", $"Летописец проспал весь год. Запись за {year}: «Ничего не случилось»");
        },
        c => { // самородок
            int p = c.Own(); if (p < 0) return null;
            c.S.Gold += 40;
            return ("diamond", $"Пастух из провинции {c.Name(p)} нашёл самородок размером с кулак. Казна +40, пастуху — новая овца");
        },
        c => { // комета
            c.MoodAll(2);
            return ("comet", "По небу пролетела хвостатая звезда. Жрецы считают её добрым знаком, астрологи — очень добрым. Довольство +2");
        },
    };

    public static int Count => Deck.Length;

    /// <summary>Deal the next applicable event and apply it. Returns its text (null if none applied).</summary>
    public static string Fire(WorldData w, GameState s, ISimSink sink)
    {
        var c = new Ctx { W = w, S = s };
        for (int tries = 0; tries < Deck.Length; tries++)
        {
            int k = s.EventCount++;
            c.Salt = k;
            var r = Deck[CardAt(w.Seed, k)](c);
            if (r is not { } e) continue;
            sink?.Notify(e.icon, e.text);
            return e.text;
        }
        return null;
    }

    /// <summary>Card index of the k-th draw: position k % n of a Fisher–Yates shuffle seeded by (seed, cycle).</summary>
    public static int CardAt(int seed, int k)
    {
        int n = Deck.Length, cycle = k / n, pos = k % n;
        Span<int> perm = stackalloc int[n];
        for (int i = 0; i < n; i++) perm[i] = i;
        for (int i = n - 1; i > 0; i--)
        {
            int j = SimRng.Pick(seed, 13, cycle, i, i + 1);
            (perm[i], perm[j]) = (perm[j], perm[i]);
        }
        return perm[pos];
    }

    static List<int> Owned(GameState s, int n)
    {
        var list = new List<int>();
        for (int p = 0; p < s.Owner.Length; p++) if (s.Owner[p] == n) list.Add(p);
        return list;
    }
}
