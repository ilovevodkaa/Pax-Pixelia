using System;
using System.Collections.Generic;
using PaxPixelia.Sim;

namespace PaxPixelia.Core;

/// <summary>
/// The «Правительство» screen's side of the policy tree (Sim/Politics.cs): what each course looks like to the player
/// now, and the orders (journaled Course commands). The ring stays «?» until «Основы государства» are laid: only then
/// does it show which way leads where.
/// </summary>
public partial class Game
{
    public enum CourseState { Adopted, Adopting, Open, Closed, Locked, Hidden }

    public readonly record struct CourseView(int Id, CourseDef Def, CourseState State, int Cycles, int Total, int Gold, int SecondsLeft);

    /// <summary>A course was started, dropped or adopted (the screen redraws, the tips rebuild).</summary>
    public event Action PoliticsChanged;
    ulong _politicsSig;

    public List<CourseView> CourseViews()
    {
        var list = new List<CourseView>(Politics.Count);
        if (!IsReady) return list;
        var nat = State.Nat[Viewer];
        bool based = Politics.Has(nat, Politics.Root);
        for (int c = 0; c < Politics.Count; c++)
        {
            var d = Politics.All[c];
            CourseState st;
            if (Politics.Has(nat, c)) st = CourseState.Adopted;
            else if (nat.CourseNow == c) st = CourseState.Adopting;
            else if (d.Ring > 0 && !based) st = CourseState.Hidden;
            else if (Politics.Shut(nat, c)) st = CourseState.Closed;
            else if (Politics.CheckStart(State, Viewer, c) is CourseError.None or CourseError.Busy) st = CourseState.Open;
            else st = CourseState.Locked;
            int total = Politics.Total(c, State.Pace), done = nat.CourseNow == c ? nat.CourseCycles : 0;
            int secs = (int)Math.Ceiling(CyclesToSeconds(Math.Max(0, total - done)));
            list.Add(new CourseView(c, d, st, done, total, Politics.GoldFor(c, nat), secs));
        }
        return list;
    }

    /// <summary>The course under way for the player, or -1.</summary>
    public int CourseNow => IsReady ? State.Nat[Viewer].CourseNow : -1;

    /// <summary>Where the player's nation stands on the compass (X right, Y authority).</summary>
    public (int X, int Y) CompassPosition => IsReady ? Politics.Position(State.Nat[Viewer]) : (0, 0);

    /// <summary>Has the nation laid «Основы государства» (the compass shows its ways)?</summary>
    public bool StateFounded => IsReady && Politics.Has(State.Nat[Viewer], Politics.Root);

    /// <summary>Why course c cannot be started now (Russian), or null.</summary>
    public string CourseProblem(int c)
    {
        if (!IsReady) return "Мир ещё не создан";
        return CourseText(Politics.CheckStart(State, Viewer, c), c);
    }

    string CourseText(CourseError e, int c) => e switch
    {
        CourseError.None => null,
        CourseError.Done => "Этот курс уже принят",
        CourseError.Busy => $"Сейчас принимается «{Politics.All[State.Nat[Viewer].CourseNow].Name}»: один курс за раз",
        CourseError.Locked => "Сначала заложите «Основы государства»",
        CourseError.Closed => "Путь закрыт: держава уже пошла в другую сторону",
        CourseError.NoCapital => "У кочующего рода ещё нет государства: сначала основайте столицу",
        _ => "Этот курс сейчас не принять",
    };

    public void AdoptCourse(int c)
    {
        if (!IsReady || (uint)c >= (uint)Politics.Count) return;
        var why = CourseProblem(c);
        if (why != null) { ShowRefusal(why); return; }
        int r = Issue(Cmd.Course(Viewer, c, true));
        if (r != 0) { ShowRefusal(CourseText((CourseError)r, c) ?? "Этот курс сейчас не принять"); return; }
        var v = Politics.All[c];
        ShowToast($"Держава принимает курс «{v.Name}»: ≈{(int)Math.Ceiling(CyclesToSeconds(Politics.Total(c, State.Pace)))} с");
    }

    public void DropCourse()
    {
        if (!IsReady || CourseNow < 0) return;
        string name = Politics.All[CourseNow].Name;
        if (Issue(Cmd.Course(Viewer, CourseNow, false)) == 0) ShowToast($"Курс «{name}» оставлен: начатое пропало");
    }

    /// <summary>Raise PoliticsChanged when the player's courses changed (after a command and on rules cycles).</summary>
    internal void CheckPolitics()
    {
        if (!IsReady) return;
        var nat = State.Nat[Viewer];
        ulong sig = (ulong)(nat.CourseNow + 1) * 0x9E3779B97F4A7C15UL;
        if (nat.Courses != null) foreach (ulong v in nat.Courses) sig = (sig ^ v) * 0x100000001B3UL;
        if (sig == _politicsSig) return;
        _politicsSig = sig;
        PoliticsChanged?.Invoke();
    }
}
