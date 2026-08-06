using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace TargetSchedulerManager.App.Controls;

/// <summary>
/// A WinForms-style NumericUpDown: a TextBox with a stacked chevron pair, owning its own layout.
/// WinUI 3 ships no NumericUpDown; its <see cref="NumberBox"/> replacement hard-codes three widths
/// (120 px input minimum, 76 px inline chevron pair, 72 px reserved text column in the inner template)
/// that make a narrow inline control unreachable without per-instance template surgery — attempted and
/// abandoned 2026-07-26 (three failed visual passes; see DOMAIN.md → WinUI gotchas). Behavior mirrors
/// the WinForms control: <see cref="RealValue"/> clamped to [<see cref="Minimum"/>, <see cref="Maximum"/>],
/// chevrons and Up/Down arrow keys step by <see cref="SmallChange"/> (committing any typed text first),
/// typed input commits on focus loss or Enter, and unparseable input reverts to the current value.
/// Integer by default; <see cref="DecimalPlaces"/> = 1 admits tenths (openspec project-scoped-tonight:
/// the Floor knob mirrors TS's Real <c>minimumaltitude</c>, and a fill must never round a stored value).
/// </summary>
public sealed class UpDownBox : Grid
{
    private readonly TextBox _text;
    private double _value;

    /// <remarks>Set <see cref="Minimum"/>/<see cref="Maximum"/> before <see cref="Value"/> in XAML —
    /// the Value setter clamps against them.</remarks>
    public double Minimum { get; set; }
    public double Maximum { get; set; } = double.MaxValue;
    public int SmallChange { get; set; } = 1;

    /// <summary>Fractional digits accepted and displayed: 0 (default, whole numbers — typed decimals
    /// round on commit) or 1 (tenths; whole values still display bare — "30", not "30.0").</summary>
    public int DecimalPlaces { get; set; }

    /// <summary>Current committed value; assignment rounds to <see cref="DecimalPlaces"/>, clamps to
    /// range, and refreshes the text.</summary>
    public double RealValue
    {
        get => _value;
        set
        {
            _value = Math.Clamp(Math.Round(value, DecimalPlaces), Minimum, Maximum);
            _text.Text = _value.ToString(DecimalPlaces == 0 ? "0" : "0.#");
        }
    }

    /// <summary>Whole-number view of <see cref="RealValue"/> — the XAML-friendly property every
    /// integer knob sets; reads round.</summary>
    public int Value
    {
        get => (int)Math.Round(_value);
        set => RealValue = value;
    }

    public UpDownBox()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _text = new TextBox
        {
            Text = "0",
            MinWidth = 0,
            Padding = new Thickness(6, 4, 4, 5),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _text.LostFocus += (_, _) => Commit();
        _text.KeyDown += Text_KeyDown;
        Children.Add(_text);

        Grid spins = new() { Margin = new Thickness(2, 0, 0, 0) };
        spins.RowDefinitions.Add(new RowDefinition());
        spins.RowDefinitions.Add(new RowDefinition());
        spins.Children.Add(MakeSpin("\uE70E", +1, row: 0));   // ChevronUp — the NumberBox glyphs
        spins.Children.Add(MakeSpin("\uE70D", -1, row: 1));   // ChevronDown
        SetColumn(spins, 1);
        Children.Add(spins);
    }

    private RepeatButton MakeSpin(string glyph, int sign, int row)
    {
        RepeatButton spin = new()
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 8 },   // FontIcon picks the symbol theme font itself
            Width = 18,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(0, row == 0 ? 0 : 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            IsTabStop = false,
            CornerRadius = new CornerRadius(2),
        };
        spin.Click += (_, _) => Step(sign);
        SetRow(spin, row);
        return spin;
    }

    // WinForms behavior: stepping commits any typed text first, so ↑ after typing "100" gives 100+step.
    private void Step(int sign)
    {
        Commit();
        RealValue = _value + sign * SmallChange;
    }

    // Parse-or-revert; the RealValue setter rounds, clamps and rewrites the text either way, so
    // out-of-range and unparseable input alike are visibly corrected (the old NumberBox
    // InvalidInputOverwritten contract).
    private void Commit() => RealValue = double.TryParse(_text.Text, out double typed) ? typed : _value;

    private void Text_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Up) { Step(+1); e.Handled = true; }
        else if (e.Key == VirtualKey.Down) { Step(-1); e.Handled = true; }
        else if (e.Key == VirtualKey.Enter) { Commit(); e.Handled = true; }
    }
}
