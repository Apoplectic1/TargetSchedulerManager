using Astronomy.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace TargetSchedulerManager.App.Support;

/// <summary>
/// Ctrl+N diagnostics window, ported from TargetPlanner's UserObservationDialog. A separate modeless
/// always-on-top <see cref="Window"/> (NOT a ContentDialog — that would block the main UI, defeating the
/// point: the open period brackets the user's actions in tsm.log between USER_OBS_START and USER_OBS_END,
/// so intervening DIAG lines are chronologically scoped). OK captures notes + a context snapshot + a
/// screenshot of the main window; blank notes = "all-okay checkpoint". Singleton: Ctrl+N while open
/// focuses the existing window instead of stacking a second START marker.
///
/// <para><b>Capture</b> grabs the main window on demand and stays open, so one session can interleave
/// several timestamped shots with notes (capture → change UI → capture → type → OK). Every shot's PNG is
/// stamped in local time (matching tsm.log), and a USER_OBS_CAP line records each, so images and notes can be
/// ordered against each other after the fact. (TP's dialog shoots only at OK; the repeatable button is TSM's.)</para>
///
/// <para>The window is the TSM-side UI; the underlying log protocol (USER_OBS_START/END/CAP markers) is the
/// shared Astronomy.Diagnostics contract and keeps its name.</para>
///
/// Built in code rather than XAML — WinUI controls construct imperatively exactly like WinForms, and this
/// stays close to the TP original it ports.
/// </summary>
internal sealed class DiagnosticsWindow : Window
{
    private static DiagnosticsWindow? _current;

    private readonly string _id;
    private readonly Window _owner;
    private readonly Func<string> _contextProvider;
    private readonly TextBox _notes;
    private readonly TextBlock _status;
    private int _captureCount;
    // True when END/CANCEL was logged from a button handler; stops Closed from double-logging.
    private bool _terminationLogged;

    private DiagnosticsWindow(Window owner, Func<string> contextProvider)
    {
        _id = Guid.NewGuid().ToString("N")[..4];
        _owner = owner;
        _contextProvider = contextProvider;

        Title = $"Diagnostics (id={_id})";
        AppWindow.Resize(new SizeInt32(660, 360));   // wide enough for the button row + "captured N (delayed) · hh:mm:ss"
        CenterOverOwner();              // TP's StartPosition.CenterParent — default placement can land on another monitor
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
        // Ctrl+Enter commits from inside the notes box (TP convention, inverted: there Enter=OK and
        // Ctrl+Enter=newline; here Enter=newline). Handled in KeyDown because the TextBox consumes Enter
        // before a button KeyboardAccelerator would see it.
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

        DiagnosticsWindow w = new(owner, contextProvider);
        _current = w;
        Log.UserObservationStart(w._id);
        w.Activate();
        w._notes.Focus(FocusState.Programmatic);
    }

    // Take a mid-session shot and stay open: grab the main window (this window hidden so it's not in its own
    // shot), re-show, and record a USER_OBS_CAP line + bump the status readout. Repeatable.
    private async void OnCaptureClick(object sender, RoutedEventArgs e)
    {
        string? path = await CaptureHidingSelfAsync(reshow: true);
        RecordCapture(path, delayed: false);
    }

    // The delayed variant: same grab, but the hidden period is 5 s instead of a DWM settle — time to open a
    // flyout / context menu on the main window, which then survives into the shot (no focus change at capture).
    // The window is hidden for the whole countdown, so a second click can't stack timers.
    private async void OnDelayedCaptureClick(object sender, RoutedEventArgs e)
    {
        string? path = await CaptureHidingSelfAsync(reshow: true, delayMs: 5000);
        RecordCapture(path, delayed: true);
    }

    private void RecordCapture(string? path, bool delayed)
    {
        if (path is not null)
        {
            _captureCount++;
            Log.UserObservationCapture(_id, path);
            _status.Text = $"captured {_captureCount}{(delayed ? " (delayed)" : string.Empty)} · {DateTime.Now:HH:mm:ss}";
        }
        else
        {
            _status.Text = "capture failed — see tsm.log";
        }
    }

    private async void OnOkClick(object sender, RoutedEventArgs e)
    {
        string? screenshotPath = await CaptureHidingSelfAsync(reshow: false);   // a final shot tied to the note

        string ctx = string.Empty;
        try { ctx = _contextProvider() ?? string.Empty; }
        catch (Exception ex) { Log.Warn("Observation contextProvider threw", ex); }

        Log.UserObservationEnd(_id, ctx, _notes.Text, screenshotPath ?? string.Empty);
        _terminationLogged = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Log.UserObservationCancel(_id);
        _terminationLogged = true;
        Close();
    }

    private void OnClosed(object sender, WindowEventArgs e)
    {
        if (!_terminationLogged)
        {
            Log.UserObservationCancel(_id);
            _terminationLogged = true;
        }
        if (ReferenceEquals(_current, this)) _current = null;
    }

    private static bool IsCtrlDown() =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    // Center over the owner window so the dialog appears where the user is looking (and never on a
    // different monitor); both AppWindow rects are physical pixels, so the math is direct.
    private void CenterOverOwner()
    {
        try
        {
            PointInt32 oPos = _owner.AppWindow.Position;
            SizeInt32 oSize = _owner.AppWindow.Size;
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

    // Hide this always-on-top window, let its fade-out + a DWM recomposite settle, grab the owner's pixels,
    // then (for a mid-session Capture) bring this window back and refocus the notes. 450 ms: 150 ms left a
    // translucent ghost of this window in the shot (observed 2026-06-10); 450 ms grabs clean. A longer
    // delayMs turns the hidden period into the delayed-capture countdown.
    private async Task<string?> CaptureHidingSelfAsync(bool reshow, int delayMs = 450)
    {
        AppWindow.Hide();
        await Task.Delay(delayMs);
        string? path = TryCaptureScreenshot();
        if (reshow)
        {
            AppWindow.Show();
            Activate();
            _notes.Focus(FocusState.Programmatic);
        }
        return path;
    }

    // Adapt the owner's physical-pixel bounds to the shared screen capture + the shared obs-<id>-<stamp> filename
    // convention; Astronomy.Diagnostics owns the grab, encode, local-time stamp, and best-effort failure path.
    private string? TryCaptureScreenshot()
    {
        PointInt32 pos = _owner.AppWindow.Position;
        SizeInt32 size = _owner.AppWindow.Size;
        return ScreenCapture.ToPng(pos.X, pos.Y, size.Width, size.Height, Log.NewObservationScreenshotPath(_id));
    }
}
