using System;
using System.Linq;
using PaxPixelia.Core;
using PaxPixelia.Core.Save;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;

namespace PaxPixelia.Tests;

/// <summary>The ruler (traits, age, death and the heir) and the faith (dogma slots, effects, conversion), saved and used by bots.</summary>
public static class LeaderFaithTests
{
    const int Me = GameState.LocalPlayer;

    public static void Run(WorldData w)
    {
        Section("ruler: the throne");
        var s = Fresh(w);
        var nat = s.Nat[Me];
        Check(s.Nat.All(x => x.Rulers == 1 && x.RulerTraits != 0), "every nation starts with a ruler of one or two traits");
        Check(Leader.Title(nat).Length >= 3 && Leader.Age(s, nat) >= 18, $"{Leader.Title(nat)}, {Leader.Age(s, nat)} years: {Leader.TraitList(nat)}");
        Check(s.Nat.Select(x => Leader.Name(x.RulerSeed)).Distinct().Count() >= s.Nat.Length / 2, "the rulers have different names");
        Check(Leader.Traits.All(t => t.Effect.Length > 0) && Leader.Traits.Count(t => t.Group == "character") >= 10, $"{Leader.Count} traits, every one with its effect");

        Section("ruler: traits work");
        var t = Fresh(w);
        var tn = t.Nat[Me];
        tn.RulerTraits = 0; Leader.Refresh(tn);
        tn.ScienceRate = 100;
        int r0 = Techs.ResearchRate(tn), price0 = Rules.BuildPrice(Core.Data.Bld.Farm, tn), mood0 = Techs.Sum(tn, TechFx.Mood);
        tn.RulerTraits = 1 << Leader.Index("wise") | 1 << Leader.Index("architect") | 1 << Leader.Index("generous");
        Leader.Refresh(tn);
        Check(Techs.ResearchRate(tn) == r0 + r0 * 10 / 100, $"«Мудрый»: research {r0} → {Techs.ResearchRate(tn)}");
        Check(Rules.BuildPrice(Core.Data.Bld.Farm, tn) < price0, $"«Зодчий»: a farm {price0} → {Rules.BuildPrice(Core.Data.Bld.Farm, tn)} gold");
        Check(Techs.Sum(tn, TechFx.Mood) == mood0 + 3, "«Щедрый»: mood +3 through Techs.Sum");
        tn.RulerTraits = 1 << Leader.Index("stern"); Leader.Refresh(tn);
        Check(Unrest.RevoltAtFor(tn) == Unrest.RevoltAt * 125 / 100, $"«Суровый»: a rising ripens {Unrest.RevoltAtFor(tn)} cycles instead of {Unrest.RevoltAt}");

        Section("ruler: death and the heir");
        var d = Fresh(w);
        var dn = d.Nat[Me];
        dn.RulerTraits = 0; Leader.Refresh(dn);
        dn.RulerAge0 = 90; dn.RulerLife = 60;   // overdue
        var twin = Fresh(w);   // the same realm with a ruler in good health
        twin.Nat[Me].RulerTraits = 0; Leader.Refresh(twin.Nat[Me]);
        twin.Nat[Me].RulerAge0 = 20; twin.Nat[Me].RulerLife = 90;
        int seed0 = dn.RulerSeed, cap = d.NationCapital[Me];
        RunCycles(w, d, Leader.YearCycles + 1);
        RunCycles(w, twin, Leader.YearCycles + 1);
        Check(dn.Rulers == 2 && (dn.RulerSeed != seed0 || dn.RulerNumeral == 2) && Leader.Age(d, dn) < 40, $"the old ruler dies, the heir takes the throne: {Leader.Title(dn)}, {Leader.Age(d, dn)}");
        Check(d.Mood[cap] <= twin.Mood[cap] - Leader.MournMood + 2, $"the realm mourns: capital mood {d.Mood[cap]} against {twin.Mood[cap]} without a death");
        var a = Fresh(w);
        a.Nat[Me].Dogmas = 1 << Faith.Index("ancestor_worship"); Faith.Refresh(a.Nat[Me]);
        a.Nat[Me].RulerAge0 = 90; a.Nat[Me].RulerLife = 60;
        int capA = a.NationCapital[Me]; a.Mood[capA] = 60;
        RunCycles(w, a, Leader.YearCycles + 1);
        Check(a.Nat[Me].Rulers == 2 && a.Mood[capA] >= 58, "«Почитание предков»: no mourning");
        var life = Fresh(w);
        int dead = 0;
        RunCycles(w, life, Leader.YearCycles * 60);
        dead = life.Nat.Sum(x => x.Rulers - 1);
        Check(dead > 0, $"in 60 ruler's years ({Leader.YearCycles * 60 / 120} min at speed 3) rulers die and heirs follow: {dead} successions");

        Section("faith: dogmas");
        var f = Fresh(w);
        var fn = f.Nat[Me];
        int fire = Faith.Index("fire_temples"), groves = Faith.Index("sacred_groves"), ascetic = Faith.Index("asceticism"), feast = Faith.Index("harvest_feast");
        Techs.Set(fn, Techs.Index("ancestors"), false); Techs.Set(fn, Techs.Index("priesthood"), false);
        Check(Faith.CheckAdopt(fn, groves) == DogmaError.NoSlot, "no teaching before «Духи предков»");
        Techs.Set(fn, Techs.Index("ancestors"), true);
        Check(Commands.Apply(w, f, Cmd.Dogma(Me, ascetic), null) == 0 && Faith.Has(fn, ascetic), "a teaching is adopted by command");
        Check(Commands.Apply(w, f, Cmd.Dogma(Me, groves), null) == (int)DogmaError.NoSlot, "one teaching only, for good");
        Check(Faith.CheckAdopt(fn, feast) != DogmaError.None, "«Праздник урожая» argues with «Аскеза»");
        Check(Firsts.Holder(f, Firsts.FirstTeaching) == Me, "the world first «Первое учение» is ours");
        Techs.Set(fn, Techs.Index("priesthood"), true);
        int shrine0 = Techs.Sum(fn, TechFx.ShrineMood);
        Check(Commands.Apply(w, f, Cmd.Dogma(Me, fire), null) == 0 && Techs.Sum(fn, TechFx.ShrineMood) == shrine0 + 3, "«Огненные храмы»: shrines +3 mood");
        Check(Faith.CheckAdopt(fn, Faith.Index("tolerance")) == DogmaError.NoSlot, "the preaching waits for Античность");
        Check(Character.Value(fn, Character.Faith) < 0, "dogmas make the people pious");

        Section("faith: conversion and tolerance");
        var c = Fresh(w);
        int p = Enumerable.Range(0, w.P).First(q => c.Owner[q] == Me && c.CapitalOf[q] < 0);
        int other = (c.Nations[Me].Religion + 1) % Data.Religions.Length;
        c.Religion[p] = (sbyte)other;
        int cycles = 0;
        while (c.Religion[p] == other && cycles < 4000) { RunCycles(w, c, 16); cycles += 16; }
        Check(c.Religion[p] == c.Nations[Me].Religion, $"a province of another faith converts ({cycles} cycles)");
        var tol = Fresh(w);
        int q2 = Enumerable.Range(0, w.P).First(q => tol.Owner[q] == Me && tol.CapitalOf[q] < 0);
        var tolNat = tol.Nat[Me];
        tolNat.Era = 2; tolNat.Dogmas = 1 << Faith.Index("tolerance"); Faith.Refresh(tolNat);
        Check(Faith.Tolerant(tolNat), "«Терпимость»: other faiths do not sulk");

        Section("ruler and faith: saved, bots");
        var sv = Fresh(w);
        sv.Nat[Me].Dogmas = 1 << groves;
        var back = SaveFile.Restore(SaveFile.Snapshot(sv), w, TestContent.Db, 100);
        Check(back.Nat[Me].RulerSeed == sv.Nat[Me].RulerSeed && back.Nat[Me].RulerTraits == sv.Nat[Me].RulerTraits && back.Nat[Me].Dogmas == sv.Nat[Me].Dogmas
              && Leader.Fx(back.Nat[Me], TechFx.Mood) == Leader.Fx(sv.Nat[Me], TechFx.Mood), "the ruler and the dogmas survive a save");
        var bots = NationGen.CreateInitialState(w);
        foreach (var x in bots.Nat) x.Control = NationControl.Bot;
        Simulation.Begin(w, bots);
        RunCycles(w, bots, 2400);
        int withDogma = bots.Nat.Count(x => x.Dogmas != 0);
        Check(withDogma >= bots.Nat.Length / 2, $"in 20 min {withDogma} of {bots.Nat.Length} bots adopt a dogma");
    }

    static void RunCycles(WorldData w, GameState s, int cycles)
    {
        for (int done = 0; done < cycles;)
        {
            if (Clock.IsCycleTick(s.Tick)) done++;
            Simulation.Step(w, s, null);
        }
    }

    static GameState Fresh(WorldData w)
    {
        var s = NationGen.CreateInitialState(w);
        s.Nat[Me].Control = NationControl.Human;
        Simulation.Begin(w, s);
        return s;
    }
}
