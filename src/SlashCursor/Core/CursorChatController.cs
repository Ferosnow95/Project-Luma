using System;
using System.Windows;
using System.Windows.Threading;
using SlashCursor.Capture;
using SlashCursor.Input;
using SlashCursor.Overlay;
using SlashCursor.Providers;
using Application = System.Windows.Application;

namespace SlashCursor.Core;

/// <summary>
/// Wires the global "/" trigger to context capture and the bubble UI.
/// Owns the active <see cref="IResponseProvider"/> (swappable at runtime).
/// </summary>
public sealed class CursorChatController : IDisposable
{
    private readonly GlobalKeyboardHook _hook = new();
    private readonly Dispatcher _dispatcher;
    private BubbleWindow? _activeBubble;

    public IResponseProvider Provider { get; set; }

    public CursorChatController(IResponseProvider provider)
    {
        Provider = provider;
        _dispatcher = Application.Current.Dispatcher;
        _hook.SlashPressed = OnSlashPressed;
        _hook.EscapePressed = OnEscapePressed;
    }

    public void Start() => _hook.Install();

    /// <summary>Guaranteed dismissal: Esc closes the bubble even if WPF focus failed.</summary>
    private void OnEscapePressed()
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (_activeBubble is { IsVisible: true } bubble)
                bubble.Close();
        });
    }

    /// <summary>
    /// Runs on the hook thread (our UI thread's message loop). Must return
    /// QUICKLY: <c>true</c> swallows the "/", <c>false</c> lets it through.
    /// All heavy work (screenshot, window creation) is deferred to the
    /// dispatcher so Windows never drops the low-level hook for being slow.
    /// </summary>
    private bool OnSlashPressed()
    {
        // If the bubble is already open, let "/" be typed into it normally.
        if (_activeBubble is { IsVisible: true })
            return false;

        // Don't hijack "/" while the user is typing in a real text field.
        if (FocusGuard.IsTextInputFocused())
            return false;

        // Only do the cheap part here: grab the cursor position.
        var (x, y) = ScreenCapture.GetCursorPosition();

        // Defer screenshot + UI off the hook callback.
        _dispatcher.BeginInvoke(() =>
        {
            try
            {
                var settings = SettingsStore.Current;
                var shot = settings.CaptureWholeScreen
                    ? ScreenCapture.CaptureWholeScreen()
                    : ScreenCapture.CaptureAroundCursor(x, y, settings.CaptureBoxSize);
                var context = new CursorContext
                {
                    CursorX = x,
                    CursorY = y,
                    ScreenshotPng = shot,
                };
                ShowBubble(context);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to show bubble", ex);
            }
        });

        return true; // swallow the "/"
    }

    private void ShowBubble(CursorContext context)
    {
        _activeBubble?.Close();
        var bubble = new BubbleWindow(Provider, context);
        bubble.Closed += (_, _) => { if (_activeBubble == bubble) _activeBubble = null; };
        _activeBubble = bubble;
        bubble.Show();
        bubble.Activate();
    }

    public void Dispose()
    {
        _hook.Dispose();
        _activeBubble?.Close();
    }
}
