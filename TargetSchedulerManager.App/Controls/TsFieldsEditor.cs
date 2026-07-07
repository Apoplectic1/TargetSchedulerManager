using System.Globalization;
using Astronomy.Catalog.TargetScheduler;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TargetSchedulerManager.App.Controls;

/// <summary>
/// The schema-generated edit form for one TS row: given a table + the seeded current values, it renders the
/// editable (cadence-safe) fields of <see cref="TsEditableSchema"/> in schema order — no field-specific UI code
/// anywhere, so adding a field to the reference lights it up here for free. Control per <see cref="TsFieldType"/>:
/// Bool→ToggleSwitch, Whole/Real→NumberBox (schema Min/Max clamp), Enum→ComboBox (schema enum map), Text→TextBox.
/// Each field commits independently on change/focus-loss through the injected callback (the guarded gate);
/// a failed commit reverts the control to its last-known value. There is never uncommitted state, so the
/// hosting flyout can light-dismiss at any time.
/// </summary>
internal sealed class TsFieldsEditor : UserControl
{
    /// <summary>Commits one column's new value; returns whether the write applied (verified). The caller maps
    /// this onto the guarded gate and any in-grid mirror of the field.</summary>
    public delegate Task<bool> CommitField(string column, object? value);

    /// <summary>The current resolved value behind a sentinel column (e.g. exposure → the row's effective
    /// seconds), or null when unknowable (a camera-side default). Re-consulted after a sentinel write, so it
    /// must reflect the caller's freshest state — not a snapshot from flyout-open time.</summary>
    public delegate double? EffectiveValue(string column);

    private readonly CommitField _commit;
    private readonly Dictionary<string, object?> _lastKnown;   // last committed (or seeded) raw value per column
    private readonly EffectiveValue? _effective;               // resolved values behind sentinel columns
    private bool _reverting;                                   // suppresses commit while a control is put back

    private TsFieldsEditor(
        TsTable table, string title, IReadOnlyDictionary<string, object?> seed, CommitField commit,
        EffectiveValue? effective)
    {
        _commit = commit;
        _lastKnown = new Dictionary<string, object?>(seed, StringComparer.OrdinalIgnoreCase);
        _effective = effective;

        StackPanel panel = new() { Spacing = 8, MinWidth = 260, Padding = new Thickness(4) };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            Margin = new Thickness(0, 0, 0, 4),
        });

        // Two columns: label | control(+unit). Rows added per field below.
        Grid form = new() { ColumnSpacing = 12, RowSpacing = 8 };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        int rowIndex = 0;
        foreach (TsField field in TsEditableSchema.For(table))
        {
            // Cadence-breaking fields wait for their confirm-dialog flow (cadence-safe-ts-edits); fields the
            // open db lacks were omitted from the seed (schema drift) and get no control either.
            if (TsEditableSchema.IsCadenceBreaking(table, field.Column)) continue;
            if (!seed.ContainsKey(field.Column)) continue;

            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock label = new() { Text = field.Label, VerticalAlignment = VerticalAlignment.Center };
            if (field.Notes is not null) ToolTipService.SetToolTip(label, field.Notes);
            Grid.SetRow(label, rowIndex);
            Grid.SetColumn(label, 0);
            form.Children.Add(label);

            FrameworkElement input = BuildControl(field, seed[field.Column]);
            if (field.Notes is not null) ToolTipService.SetToolTip(input, field.Notes);
            // Sentinel controls place the unit beside their inner box themselves.
            FrameworkElement cell = field.Unit is null || field.Sentinel is not null ? input : WithUnit(input, field.Unit);
            Grid.SetRow(cell, rowIndex);
            Grid.SetColumn(cell, 1);
            form.Children.Add(cell);

            rowIndex++;
        }

        panel.Children.Add(form);
        Content = panel;
    }

    /// <summary>Builds the form, or an error placeholder when the seed is null (row missing / read fault) —
    /// never a form with fabricated values. <paramref name="effective"/> optionally resolves a sentinel
    /// column's current effective value (for the "use default (…)" label and the box after a revert).</summary>
    public static UIElement Create(
        TsTable table, string title, IReadOnlyDictionary<string, object?>? seed, CommitField commit,
        EffectiveValue? effective = null) =>
        seed is null
            ? new TextBlock
            {
                Text = $"{title}\nCouldn't read current values from the TS db — see tsm.log.",
                MaxWidth = 300,
                TextWrapping = TextWrapping.Wrap,
            }
            : new TsFieldsEditor(table, title, seed, commit, effective);

    private FrameworkElement BuildControl(TsField field, object? seeded) => field.Type switch
    {
        TsFieldType.Bool => BuildToggle(field, seeded),
        TsFieldType.Whole or TsFieldType.Real when field.Sentinel is not null => BuildSentinelNumber(field, seeded),
        TsFieldType.Whole or TsFieldType.Real => BuildNumber(field, seeded),
        TsFieldType.Enum => BuildCombo(field, seeded),
        _ => BuildText(field, seeded),
    };

    private ToggleSwitch BuildToggle(TsField field, object? seeded)
    {
        ToggleSwitch toggle = new()
        {
            IsOn = ToLong(seeded) != 0,
            OnContent = null, OffContent = null,
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toggle.Toggled += async (_, _) =>
        {
            if (_reverting) return;
            int wanted = toggle.IsOn ? 1 : 0;
            if (ToLong(_lastKnown[field.Column]) == wanted) return;
            if (await _commit(field.Column, wanted))
                _lastKnown[field.Column] = (long)wanted;
            else
                Revert(() => toggle.IsOn = ToLong(_lastKnown[field.Column]) != 0);
        };
        return toggle;
    }

    private NumberBox BuildNumber(TsField field, object? seeded)
    {
        NumberBox box = new()
        {
            Value = ToDouble(seeded),
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
            Width = 110,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // ValueChanged, not LostFocus: inside a flyout, focus rarely leaves the box before dismissal, which
        // made typed edits commit only at close while toggle/checkbox edits committed instantly. ValueChanged
        // fires when the value is CONFIRMED (Enter / focus loss / spin) — same immediacy as the toggles.
        box.ValueChanged += async (_, _) =>
        {
            if (_reverting) return;
            double current = ToDouble(_lastKnown[field.Column]);
            if (double.IsNaN(box.Value)) { Revert(() => box.Value = current); return; }   // cleared → put back

            // Clamp to the schema bounds so no out-of-range value reaches the gate.
            double wanted = box.Value;
            if (field.Min is double min && wanted < min) wanted = min;
            if (field.Max is double max && wanted > max) wanted = max;
            if (field.Type == TsFieldType.Whole) wanted = Math.Round(wanted);
            Revert(() => box.Value = wanted);
            if (wanted == current) return;

            object value = field.Type == TsFieldType.Whole ? (int)wanted : wanted;
            if (await _commit(field.Column, value))
                _lastKnown[field.Column] = value;
            else
                Revert(() => box.Value = ToDouble(_lastKnown[field.Column]));
        };
        return box;
    }

    // A numeric column with TS's defer-to-default sentinel (a reserved -1 meaning "resolve elsewhere"): rendered
    // as its meaning — a "use <default> checkbox" over the number box — never as the raw -1. Checked ⇔ the column
    // holds the sentinel (box disabled, showing the resolved value when the caller can know it); unchecked ⇔ the
    // column holds the visible number. The sentinel is exempt from Min/Max clamping (writing it back must work).
    private FrameworkElement BuildSentinelNumber(TsField field, object? seeded)
    {
        double sentinel = field.Sentinel!.Value;
        bool isDefault = ToDouble(seeded) == sentinel;
        // The provider is only trustworthy as "the default" while the column actually holds the sentinel — an
        // overridden plan's effective value is the override itself, which must not masquerade as the default.
        double? effective = isDefault ? _effective?.Invoke(field.Column) : null;

        string LabelFor(double? value) => value is double v
            ? $"{field.SentinelLabel} ({v:0.###}{(field.Unit is null ? "" : " " + field.Unit)})"
            : field.SentinelLabel ?? "default";

        CheckBox useDefault = new()
        {
            Content = LabelFor(effective),
            IsChecked = isDefault,
            MinWidth = 0,
        };

        NumberBox box = new()
        {
            Value = isDefault ? effective ?? double.NaN : ToDouble(seeded),
            IsEnabled = !isDefault,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
            Width = 110,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Checkbox drives the sentinel: checking writes it; unchecking only arms the box (the real value
        // commits when the user commits a number — no silent -1 → value conversion on a stray click).
        useDefault.Checked += async (_, _) =>
        {
            if (_reverting) return;
            box.IsEnabled = false;
            if (ToDouble(_lastKnown[field.Column]) == sentinel) return;   // seeded state settling, not an edit
            object value = field.Type == TsFieldType.Whole ? (int)sentinel : sentinel;
            if (await _commit(field.Column, value))
            {
                _lastKnown[field.Column] = value;
                // The column now holds the sentinel, so the provider's value IS the default (the commit path
                // resolved it) — show it in the box and label right away, no flyout relaunch needed.
                effective = _effective?.Invoke(field.Column) ?? effective;
                useDefault.Content = LabelFor(effective);
                Revert(() => box.Value = effective ?? double.NaN);
            }
            else
            {
                Revert(() =>
                {
                    useDefault.IsChecked = false;
                    box.IsEnabled = true;
                    box.Value = ToDouble(_lastKnown[field.Column]);
                });
            }
        };
        useDefault.Unchecked += (_, _) =>
        {
            if (_reverting) return;
            box.IsEnabled = true;
            Revert(() => box.Value = effective ?? double.NaN);   // seed the override with the resolved value
            box.Focus(FocusState.Programmatic);
        };

        // ValueChanged, not LostFocus — same reasoning as the plain number box: a typed override must commit
        // (and mirror) the moment it's confirmed, matching the checkbox's immediacy.
        box.ValueChanged += async (_, _) =>
        {
            if (_reverting || !box.IsEnabled) return;
            double current = ToDouble(_lastKnown[field.Column]);
            if (double.IsNaN(box.Value))
            {
                // Cleared: restore the last real value, or stay blank while the column still holds the sentinel.
                if (current != sentinel) Revert(() => box.Value = current);
                return;
            }

            double wanted = box.Value;
            if (field.Min is double min && wanted < min) wanted = min;
            if (field.Max is double max && wanted > max) wanted = max;
            if (field.Type == TsFieldType.Whole) wanted = Math.Round(wanted);
            Revert(() => box.Value = wanted);
            if (wanted == current) return;

            object value = field.Type == TsFieldType.Whole ? (int)wanted : wanted;
            if (await _commit(field.Column, value))
                _lastKnown[field.Column] = value;
            else if (current == sentinel)
                Revert(() => { useDefault.IsChecked = true; box.IsEnabled = false; box.Value = effective ?? double.NaN; });
            else
                Revert(() => box.Value = current);
        };

        StackPanel inner = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        inner.Children.Add(box);
        if (field.Unit is not null)
            inner.Children.Add(new TextBlock { Text = field.Unit, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center });

        StackPanel cell = new() { Orientation = Orientation.Vertical, Spacing = 4 };
        cell.Children.Add(useDefault);
        cell.Children.Add(inner);
        return cell;
    }

    private ComboBox BuildCombo(TsField field, object? seeded)
    {
        IReadOnlyList<TsEnumValue> values = TsEditableSchema.EnumValues(field.EnumName);
        ComboBox combo = new()
        {
            ItemsSource = values,
            DisplayMemberPath = nameof(TsEnumValue.Label),
            SelectedItem = values.FirstOrDefault(v => v.Code == ToLong(seeded)),
            MinWidth = 110,
            VerticalAlignment = VerticalAlignment.Center,
        };
        combo.SelectionChanged += async (_, _) =>
        {
            if (_reverting || combo.SelectedItem is not TsEnumValue picked) return;
            if (picked.Code == ToLong(_lastKnown[field.Column])) return;
            if (await _commit(field.Column, picked.Code))
                _lastKnown[field.Column] = (long)picked.Code;
            else
                Revert(() => combo.SelectedItem = values.FirstOrDefault(v => v.Code == ToLong(_lastKnown[field.Column])));
        };
        return combo;
    }

    private TextBox BuildText(TsField field, object? seeded)
    {
        TextBox box = new()
        {
            Text = seeded?.ToString() ?? string.Empty,
            MinWidth = 140,
            VerticalAlignment = VerticalAlignment.Center,
        };
        box.LostFocus += async (_, _) =>
        {
            if (_reverting) return;
            string current = _lastKnown[field.Column]?.ToString() ?? string.Empty;
            if (box.Text == current) return;
            if (await _commit(field.Column, box.Text))
                _lastKnown[field.Column] = box.Text;
            else
                Revert(() => box.Text = _lastKnown[field.Column]?.ToString() ?? string.Empty);
        };
        return box;
    }

    private static StackPanel WithUnit(FrameworkElement input, string unit)
    {
        StackPanel cell = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        cell.Children.Add(input);
        cell.Children.Add(new TextBlock
        {
            Text = unit,
            Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return cell;
    }

    private void Revert(Action putBack)
    {
        _reverting = true;
        try { putBack(); }
        finally { _reverting = false; }
    }

    // Seed values are raw SQLite (long/double/string/null); compare and convert through invariant culture.
    private static long ToLong(object? v) => v is null ? 0 : Convert.ToInt64(v, CultureInfo.InvariantCulture);

    private static double ToDouble(object? v) => v is null ? 0 : Convert.ToDouble(v, CultureInfo.InvariantCulture);
}
