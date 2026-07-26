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
/// the WinForms control: integer <see cref="Value"/> clamped to [<see cref="Minimum"/>, <see cref="Maximum"/>],
/// chevrons and Up/Down arrow keys step by <see cref="SmallChange"/> (committing any typed text first),
/// typed input commits on focus loss or Enter, and unparseable input reverts to the current value.
/// </summary>
public sealed class UpDownBox : Grid
{
    private readonly TextBox _text;
    private int _value;

    /// <remarks>Set <see cref="Minimum"/>/<see cref="Maximum"/> before <see cref="Value"/> in XAML —
    /// the Value setter clamps against them.</remarks>
    public int Minimum { get; set; }
    public int Maximum { get; set; } = int.MaxValue;
    public int SmallChange { get; set; } = 1;

    /// <summary>Current committed value; assignment clamps to range and refreshes the text.</summary>
    public int Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, Minimum, Maximum);
            _text.Text = _value.ToString();
        }
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
        Value = _value + sign * SmallChange;
    }

    // Parse-or-revert; the Value setter clamps and rewrites the text either way, so out-of-range and
    // unparseable input alike are visibly corrected (the old NumberBox InvalidInputOverwritten contract).
    private void Commit() => Value = int.TryParse(_text.Text, out int typed) ? typed : _value;

    private void Text_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Up) { Step(+1); e.Handled = true; }
        else if (e.Key == VirtualKey.Down) { Step(-1); e.Handled = true; }
        else if (e.Key == VirtualKey.Enter) { Commit(); e.Handled = true; }
    }
}
