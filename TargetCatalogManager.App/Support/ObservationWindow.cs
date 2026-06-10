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
    // True when END/CANCEL was logged from a button handler; stops Closed from double-logging.
    private bool mTerminationLogged;

    private ObservationWindow(Window owner, Func<string> contextProvider)
    {
        mId = Guid.NewGuid().ToString("N")[..4];
        mOwner = owner;
        mContextProvider = contextProvider;

        Title = $"Observation (id={mId})";
        AppWindow.Resize(new SizeInt32(560, 340));
        CenterOverOwner();              // TP's StartPosition.CenterParent — default placement can land on another monitor
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsAlwaysOnTop = true;     // TP's TopMost: stays over the main window while you drive it
            p.IsMinimizable = false;
            p.IsMaximizable = false;
        }

        TextBlock label = new()
        {
            Text = "Notes (free-form; Enter for newline). Leave blank for a checkpoint. Ctrl+Enter = OK:",
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

        Button ok = new() { Content = "OK", MinWidth = 90, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        ok.Click += OnOkClick;

        Button cancel = new() { Content = "Cancel", MinWidth = 90 };
        cancel.Click += OnCancelClick;

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

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

    private async void OnOkClick(object sender, RoutedEventArgs e)
    {
        // Hide before the screen grab so this window isn't in its own screenshot. DWM recomposites the
        // area we were covering from the main window's surface; the short delay lets that land.
        AppWindow.Hide();
        await Task.Delay(150);

        string? screenshotPath = TryCaptureScreenshot();

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

    // Capture the owner window's screen pixels (AppWindow position/size are physical pixels — the same
    // space CopyFromScreen works in) and save a PNG under Logs\screenshots\. Screen-grab rather than
    // RenderTargetBitmap so the capture is the literal rendered truth, window chrome included.
    private string? TryCaptureScreenshot()
    {
        try
        {
            PointInt32 pos = mOwner.AppWindow.Position;
            SizeInt32 size = mOwner.AppWindow.Size;
            if (size.Width <= 0 || size.Height <= 0) return null;

            string dir = Path.Combine(Log.NotesFolderPath, "screenshots");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"obs-{mId}-{DateTime.UtcNow:yyyyMMddHHmmss}.png");

            using System.Drawing.Bitmap bmp = new(size.Width, size.Height);
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(pos.X, pos.Y, 0, 0, new System.Drawing.Size(size.Width, size.Height));
            }
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            return path;
        }
        catch (Exception ex)
        {
            Log.Warn("Observation screenshot capture failed", ex);
            return null;
        }
    }
}
