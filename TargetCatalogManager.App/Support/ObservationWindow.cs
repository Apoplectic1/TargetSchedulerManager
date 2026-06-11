using Astronomy.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace TargetCatalogManager.App.Support;

/// <summary>
/// Ctrl+N observation window, ported from TargetPlanner's UserObservationDialog. A separate modeless
/// always-on-top <see cref="Window"/> (NOT a ContentDialog — that would block the main UI, defeating the
/// point: the open period brackets the user's actions in tcm.log between USER_OBS_START and USER_OBS_END,
/// so intervening DIAG lines are chronologically scoped). OK captures notes + a context snapshot + a
/// screenshot of the main window; blank notes = "all-okay checkpoint". Singleton: Ctrl+N while open
/// focuses the existing window instead of stacking a second START marker.
///
/// <para><b>Capture</b> grabs the main window on demand and stays open, so one session can interleave
/// several timestamped shots with notes (capture → change UI → capture → type → OK). Every shot's PNG is
/// stamped in local time (matching tcm.log), and a USER_OBS_CAP line records each, so images and notes can be
/// ordered against each other after the fact. (TP's dialog shoots only at OK; the repeatable button is TCM's.)</para>
///
/// Built in code rather than XAML — WinUI controls construct imperatively exactly like WinForms, and this
/// stays close to the TP original it ports.
/// </summary>
internal sealed class ObservationWindow : Window
{
    private static ObservationWindow? sCurrent;

    private readonly string mId;
    private readonly Window mOwner;
    private readonly Func<string> mContextProvider;
    private readonly TextBox mNotes;
    private readonly TextBlock mStatus;
    private int mCaptureCount;
    // True when END/CANCEL was logged from a button handler; stops Closed from double-logging.
    private bool mTerminationLogged;

    private ObservationWindow(Window owner, Func<string> contextProvider)
    {
        mId = Guid.NewGuid().ToString("N")[..4];
        mOwner = owner;
        mContextProvider = contextProvider;

        Title = $"Observation (id={mId})";
        AppWindow.Resize(new SizeInt32(560, 360));
        CenterOverOwner();              // TP's StartPosition.CenterParent — default placement can land on another monitor
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsAlwaysOnTop = true;     // TP's TopMost: stays over the main window while you drive it
            p.IsMinimizable = false;
            p.IsMaximizable = false;
        }

        TextBlock label = new()
        {
            Text = "Notes (Enter = newline, Ctrl+Enter = OK). Capture = screenshot the main window now "
                 + "(repeatable); leave notes blank for a checkpoint:",
            TextWrapping = TextWrapping.Wrap,
        };

        mNotes = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(mNotes, ScrollBarVisibility.Auto);
        // Ctrl+Enter commits from inside the notes box (TP convention, inverted: there Enter=OK and
        // Ctrl+Enter=newline; here Enter=newline). Handled in KeyDown because the TextBox consumes Enter
        // before a button KeyboardAccelerator would see it.
        mNotes.KeyDown += (s, e) =>
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

        mStatus = new TextBlock
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
        leftButtons.Children.Add(mStatus);

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
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(label, 0);
        Grid.SetRow(mNotes, 1);
        Grid.SetRow(buttons, 2);
        root.Children.Add(label);
        root.Children.Add(mNotes);
        root.Children.Add(buttons);
        Content = root;

        Closed += OnClosed;
    }

    /// <summary>Open the observation window over <paramref name="owner"/>, or focus the existing one.
    /// <paramref name="contextProvider"/> is called at OK time so the END line carries the app state
    /// as of the moment the user committed the note, not the moment the window opened.</summary>
    public static void ShowOrFocus(Window owner, Func<string> contextProvider)
    {
        if (sCurrent is not null)
        {
            sCurrent.AppWindow.Show();
            sCurrent.Activate();
            return;
        }

        ObservationWindow w = new(owner, contextProvider);
        sCurrent = w;
        Log.UserObservationStart(w.mId);
        w.Activate();
        w.mNotes.Focus(FocusState.Programmatic);
    }

    // Take a mid-session shot and stay open: grab the main window (this window hidden so it's not in its own
    // shot), re-show, and record a USER_OBS_CAP line + bump the status readout. Repeatable.
    private async void OnCaptureClick(object sender, RoutedEventArgs e)
    {
        string? path = await CaptureHidingSelfAsync(reshow: true);
        if (path is not null)
        {
            mCaptureCount++;
            Log.UserObservationCapture(mId, path);
            mStatus.Text = $"captured {mCaptureCount} · {DateTime.Now:HH:mm:ss}";
        }
        else
        {
            mStatus.Text = "capture failed — see tcm.log";
        }
    }

    private async void OnOkClick(object sender, RoutedEventArgs e)
    {
        string? screenshotPath = await CaptureHidingSelfAsync(reshow: false);   // a final shot tied to the note

        string ctx = string.Empty;
        try { ctx = mContextProvider() ?? string.Empty; }
        catch (Exception ex) { Log.Warn("Observation contextProvider threw", ex); }

        Log.UserObservationEnd(mId, ctx, mNotes.Text, screenshotPath ?? string.Empty);
        mTerminationLogged = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Log.UserObservationCancel(mId);
        mTerminationLogged = true;
        Close();
    }

    private void OnClosed(object sender, WindowEventArgs e)
    {
        if (!mTerminationLogged)
        {
            Log.UserObservationCancel(mId);
            mTerminationLogged = true;
        }
        if (ReferenceEquals(sCurrent, this)) sCurrent = null;
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
            PointInt32 oPos = mOwner.AppWindow.Position;
            SizeInt32 oSize = mOwner.AppWindow.Size;
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
    // translucent ghost of this window in the shot (observed 2026-06-10); 450 ms grabs clean.
    private async Task<string?> CaptureHidingSelfAsync(bool reshow)
    {
        AppWindow.Hide();
        await Task.Delay(450);
        string? path = TryCaptureScreenshot();
        if (reshow)
        {
            AppWindow.Show();
            Activate();
            mNotes.Focus(FocusState.Programmatic);
        }
        return path;
    }

    // Adapt the owner's physical-pixel bounds to the shared screen capture + the shared obs-<id>-<stamp> filename
    // convention; Astronomy.Diagnostics owns the grab, encode, local-time stamp, and best-effort failure path.
    private string? TryCaptureScreenshot()
    {
        PointInt32 pos = mOwner.AppWindow.Position;
        SizeInt32 size = mOwner.AppWindow.Size;
        return ScreenCapture.ToPng(pos.X, pos.Y, size.Width, size.Height, Log.NewObservationScreenshotPath(mId));
    }
}
