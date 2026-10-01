using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Core.Nations;
using PaxPixelia.Core.Save;

namespace PaxPixelia.UI.Front;

/// <summary>
/// «Блиц недели»: this week's world for everybody (Core/Blitz.cs) — the rules in one card, the table of results in the
/// blitz folder (the player's own and friends' files), each replayed in the background and marked honest or not, the
/// folder to drop friends' files into, and «НАЧАТЬ БЛИЦ» with the player's last nation.
/// </summary>
public partial class BlitzScreen : FrontScreen
{
    string _week;
    Button _start;
    VBoxContainer _table;
    readonly System.Threading.CancellationTokenSource _cancel = new();

    public override string Title => "Блиц недели";
    public override string Subtitle => Blitz.WeekTitle(_week ??= Blitz.WeekId(DateTime.UtcNow)) + " · один мир для всех";
    public override float PanelWidth => 720;
    public override Control DefaultFocus => _start;

    public override void Build()
    {
        _week ??= Blitz.WeekId(DateTime.UtcNow);
        var rules = Card();
        rules.AddChild(PixelKit.Kicker("ПРАВИЛА", PixelKit.Secondary, 11));
        foreach (var line in new[]
        {
            $"всю неделю у всех один мир: зерно «{Blitz.SeedTextOf(_week)}», {Blitz.Nations} держав, кочевой старт;",
            $"{Blitz.Ticks / 480} минут игрового времени на скорости 3 (на скорости 5 — {Blitz.Ticks / 2400}), потом подсчёт;",
            "очки: люди, земли, эпохи, знания, города и встреченные державы;",
            "файл результата — в папке блица; положите туда файлы друзей, и игра проверит их, повторив партию.",
        })
            rules.AddChild(PixelKit.Paragraph("· " + line, 15, PixelKit.Text));

        AddChild(PixelKit.Kicker("ТАБЛИЦА НЕДЕЛИ", PixelKit.Secondary, 11));
        _table = new VBoxContainer();
        _table.AddThemeConstantOverride("separation", 6);
        AddChild(_table);
        FillTable();

        _start = MakeButton("НАЧАТЬ БЛИЦ", StartBlitz, "PrimaryButton", 260);
        var folder = MakeButton("Папка результатов", () => { BlitzStore.OpenFolder(); }, "GhostButton", 200);
        var refresh = MakeButton("Обновить", FillTable, "GhostButton", 140);
        Footer(new Control[] { MakeButton("Назад", () => GoBack(), "GhostButton", 120), folder, refresh }, new Control[] { _start });
    }

    public override void _ExitTree() => _cancel.Cancel();

    void StartBlitz()
    {
        var setup = Blitz.WeekSetup(_week, NationStore.TouchCurrent());
        Shell.StartGame(setup);
    }

    async void FillTable()
    {
        foreach (var c in _table.GetChildren()) c.QueueFree();
        var list = BlitzStore.List(_week);
        if (list.Count == 0)
        {
            _table.AddChild(PixelKit.Paragraph("Результатов этой недели пока нет. Сыграйте первым — или положите в папку файлы друзей.", 15, PixelKit.TextDim));
            return;
        }
        var marks = new List<(BlitzEntry e, Label status)>();
        string me = NationStore.Current?.Id;
        for (int i = 0; i < list.Count && i < 12; i++)
        {
            var e = list[i]; var r = e.Record;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            row.AddChild(PixelKit.Label($"{i + 1}.", 16, PixelKit.TextDim));
            row.AddChild(new ColorRect { Color = Color.Color8(r.R, r.G, r.B), CustomMinimumSize = new Vector2(14, 14), SizeFlagsVertical = SizeFlags.ShrinkCenter });
            var name = PixelKit.Label(r.NationName + (r.Setup.Player?.Id == me && me != null ? "  (вы)" : ""), 16, PixelKit.Text);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(name);
            var status = PixelKit.Label("проверяется…", 13, PixelKit.TextDim);
            row.AddChild(status);
            row.AddChild(PixelKit.Label(r.Score.Total.ToString(), 18, PixelKit.AccentLight));
            _table.AddChild(row);
            marks.Add((e, status));
        }
        if (list.Count > 12) _table.AddChild(PixelKit.Label($"и ещё {list.Count - 12}", 13, PixelKit.TextDim));
        foreach (var (e, status) in marks)
        {
            string error;
            try { error = await BlitzStore.CheckAsync(e, _cancel.Token); }
            catch (OperationCanceledException) { return; }
            if (!IsInstanceValid(status)) return;
            status.Text = error == null ? "честно" : "не сходится";
            status.AddThemeColorOverride("font_color", error == null ? PixelKit.Accent : PixelKit.TextDim);
            status.TooltipText = error ?? "Игра повторила партию по журналу: очки честные";
        }
    }
}
