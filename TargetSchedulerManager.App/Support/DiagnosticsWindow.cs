using Astronomy.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace TargetSchedulerManager.App.Support;

/// <summary>
/// Ctrl+N diagnostics window — the WinUI shell over <see cref="ObservationSession"/>, which owns the
/// orchestration (id, START/CAP/END/CANCEL sequencing, single-terminator guarantee, capture counting,
/// status wording, hide → settle → grab → reshow). This window was the model the library type was lifted
/// from (2026-07-24); what remains here is only the framework glue: the <see cref="Window"/> itself, its
/// controls, the singleton focus-existing rule, and the three delegates the session drives.
///
/// A separate modeless always-on-top <see cref="Window"/> (NOT a ContentDialog — that would block the main
/// UI, defeating the point: the open period brackets the user's actions in tsm.log between USER_OBS_START
/// and USER_OBS_END, so intervening DIAG lines are chronologically scoped). OK captures notes + a context
/// snapshot + a screenshot of the main window; blank notes = "all-okay checkpoint". Singleton: Ctrl+N
/// while open focuses the existing window instead of stacking a second START marker.
///
/// Built in code rather than XAML — WinUI controls construct imperatively exactly like WinForms, and this
/// stays close to the TP sibling (<c>DiagnosticsDialog</c>), which drives the same session type.
/// </summary>
internal sealed class DiagnosticsWindow : Window
{
    private static DiagnosticsWindow? _current;

    private readonly ObservationSession _session;
    private readonly TextBox _notes;
    private readonly TextBlock _status;

    private DiagnosticsWindow(Window owner, Func<string> contextProvider)
    {
        // The session logs START and owns the choreography; the delegates are this window's only
        // framework-specific contribution to a capture. 450 ms settle = the WinUI default (fade-out +
        // DWM recomposite; the empirical basis lives in the library type's docs).
        _session = ObservationSession.Begin(
            contextProvider,
            ownerBounds: () =>
            {
                PointInt32 pos = owner.AppWindow.Position;
                SizeInt32 size = owner.AppWindow.Size;
                return (pos.X, pos.Y, size.Width, size.Height);
            },
            hideOverlay: () => AppWindow.Hide(),
            showOverlay: () =>
            {
                AppWindow.Show();
                Activate();
                // Null-forgiving: the delegate can only run after the ctor finishes assigning _notes.
                _notes!.Focus(FocusState.Programmatic);
            });

        Title = $"Diagnostics (id={_session.Id})";
        AppWindow.Resize(new SizeInt32(660, 360));   // wide enough for the button row + "captured N (delayed) · hh:mm:ss"
        CenterOverOwner(owner);         // TP's StartPosition.CenterParent — default placement can land on another monitor
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsAlwaysOnTop = true;     // TP's TopMost: stays over the main window while you drive it
            p.IsMinimizable = false;
            p.IsMaximizable = false;
        }

        _notes = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_notes, ScrollBarVisibility.Auto);
        // Ctrl+Enter commits from inside the notes box (Enter=newline). Handled in KeyDown because the
        // TextBox consumes Enter before a button KeyboardAccelerator would see it.
        _notes.KeyDown += (s, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter && IsCtrlDown())
            {
                e.Handled = true;
                OnOkClick(this, new RoutedEventArgs());
            }
        };

        // Capture stays open and re-shows itself after the grab; OK / Cancel are terminal. Capture sits left,
        // visually apart from the terminal pair on the right.
        Button capture = new() { Content = "Capture", MinWidth = 100 };
        capture.Click += OnCaptureClick;

        // Delayed capture for transient UI (flyouts, context menus): those are light-dismiss, so they close the
        // moment this window takes focus — an immediate Capture can never contain one. This hides the window
        // right away (focus returns to the main window), leaves 5 s to open the transient state, then grabs
        // with no focus change at capture time.
        Button delayedCapture = new() { Content = "Capture in 5 s", MinWidth = 100, Margin = new Thickness(8, 0, 0, 0) };
        delayedCapture.Click += OnDelayedCaptureClick;

        _status = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.7,
            Margin = new Thickness(12, 0, 0, 0),
        };

        Button ok = new() { Content = "OK", MinWidth = 90, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        ok.Click += OnOkClick;

        Button cancel = new() { Content = "Cancel", MinWidth = 90 };
        cancel.Click += OnCancelClick;

        StackPanel leftButtons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        leftButtons.Children.Add(capture);
        leftButtons.Children.Add(delayedCapture);
        leftButtons.Children.Add(_status);

        StackPanel rightButtons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        rightButtons.Children.Add(ok);
        rightButtons.Children.Add(cancel);

        Grid buttons = new();
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(leftButtons, 0);
        Grid.SetColumn(rightButtons, 1);
        buttons.Children.Add(leftButtons);
        buttons.Children.Add(rightButtons);

        Grid root = new() { Padding = new Thickness(12), RowSpacing = 10 };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_notes, 0);
        Grid.SetRow(buttons, 1);
        root.Children.Add(_notes);
        root.Children.Add(buttons);
        Content = root;

        Closed += OnClosed;
    }

    /// <summary>Open the diagnostics window over <paramref name="owner"/>, or focus the existing one.
    /// <paramref name="contextProvider"/> is called at OK time so the END line carries the app state
    /// as of the moment the user committed the note, not the moment the window opened.</summary>
    public static void ShowOrFocus(Window owner, Func<string> contextProvider)
    {
        if (_current is not null)
        {
            _current.AppWindow.Show();
            _current.Activate();
            return;
        }

        DiagnosticsWindow w = new(owner, contextProvider);   // ObservationSession.Begin logs START
        _current = w;
        w.Activate();
        w._notes.Focus(FocusState.Programmatic);
    }

    private void OnCaptureClick(object sender, RoutedEventArgs e) => Shared.UiTask.FireAndLog(async () =>
    {
        ObservationCapture cap = await _session.CaptureAsync();
        _status.Text = cap.StatusText;
    }, "diagnostics capture");

    private void OnDelayedCaptureClick(object sender, RoutedEventArgs e) => Shared.UiTask.FireAndLog(async () =>
    {
        ObservationCapture cap = await _session.CaptureAsync(delayMs: 5000, markDelayed: true);
        _status.Text = cap.StatusText;
    }, "diagnostics delayed capture");

    private void OnOkClick(object sender, RoutedEventArgs e) => Shared.UiTask.FireAndLog(async () =>
    {
        if (await _session.CompleteAsync(_notes.Text))   // false = capture in flight; stay open, retry
            Close();
    }, "diagnostics OK");

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        _session.Cancel();
        Close();
    }

    private void OnClosed(object sender, WindowEventArgs e)
    {
        _session.Cancel();   // idempotent: a no-op after OK/Cancel, the terminator on close-X
        if (ReferenceEquals(_current, this)) _current = null;
    }

    private static bool IsCtrlDown() =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    // Center over the owner window so the dialog appears where the user is looking (and never on a
    // different monitor); both AppWindow rects are physical pixels, so the math is direct.
    private void CenterOverOwner(Window owner)
    {
        try
        {
            PointInt32 oPos = owner.AppWindow.Position;
            SizeInt32 oSize = owner.AppWindow.Size;
            SizeInt32 mine = AppWindow.Size;
            AppWindow.Move(new PointInt32(
                oPos.X + ((oSize.Width - mine.Width) / 2),
                oPos.Y + ((oSize.Height - mine.Height) / 2)));
        }
        catch
        {
            // Positioning is cosmetic; never fail the window over it.
        }
    }
}
