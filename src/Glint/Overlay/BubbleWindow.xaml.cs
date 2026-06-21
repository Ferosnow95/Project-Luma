using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Glint.Providers;

namespace Glint.Overlay;

/// <summary>
/// A full-screen, click-blocking overlay that hosts the chat bubble. The
/// overlay owns keyboard focus (so typing and Esc always work) and intercepts
/// mouse clicks (so windows underneath can't steal focus). The bubble itself is
/// a child element that continuously follows the cursor (Figma-style).
/// </summary>
public partial class BubbleWindow : Window
{
    private const int FollowOffset = 16;

    /// <summary>The kind of request the user picked from the action pills.</summary>
    private enum AskMode { Summarize, Explain, Search }

    private static readonly (string Label, AskMode Mode)[] PillDefs =
    {
        ("Summarize", AskMode.Summarize),
        ("Explain", AskMode.Explain),
        ("Search", AskMode.Search),
    };

    private readonly IResponseProvider _provider;
    private readonly CursorContext _context;
    private readonly DispatcherTimer _followTimer;
    private readonly List<Border> _pills = new();
    private int _pillIndex;
    private bool _pillMode = true;
    private AskMode _mode;
    private CancellationTokenSource? _cts;
    private bool _busy;

    public BubbleWindow(IResponseProvider provider, CursorContext context)
    {
        InitializeComponent();
        _provider = provider;
        _context = context;
        ProviderLabel.Text = provider.Name;

        _followTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(15),
        };
        _followTimer.Tick += (_, _) => PositionBubble();

        Loaded += OnLoaded;
        Closed += (_, _) => _followTimer.Stop();
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Stretch the overlay across the entire virtual desktop (all monitors).
        CoverVirtualDesktop();
        PositionBubble();
        _followTimer.Start();

        // Build the action pills and highlight the first one.
        BuildPills();
        HighlightPill();

        // Force the overlay to the foreground and take keyboard focus on the
        // window itself (the text box is hidden until a pill is chosen). A tray
        // (background) app can't reliably steal focus with Activate() alone
        // because of Windows' foreground lock, so attach to the foreground
        // thread's input queue while we take focus.
        ForceForeground();
        Activate();
        Focus();
        Keyboard.Focus(this);

        // Figma-style entrance + a one-shot ripple/sparkle at the cursor.
        PlayEntrance();
        SpawnCursorFx();
    }

    /// <summary>Creates the action pills (Summarize / Explain / Search).</summary>
    private void BuildPills()
    {
        for (int i = 0; i < PillDefs.Length; i++)
        {
            int index = i;
            var pill = new Border
            {
                Child = new TextBlock
                {
                    Text = PillDefs[i].Label,
                    FontSize = 13,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC)),
                },
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(0, 0, 8, 0),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2A, 0x2A, 0x2A)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x3A, 0x3A)),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            pill.MouseLeftButtonUp += (_, ev) => { ev.Handled = true; SelectPill(index); };
            _pills.Add(pill);
            PillBar.Children.Add(pill);
        }
    }

    /// <summary>Applies the accent style to the currently focused pill.</summary>
    private void HighlightPill()
    {
        for (int i = 0; i < _pills.Count; i++)
        {
            bool on = i == _pillIndex;
            var pill = _pills[i];
            pill.Background = new SolidColorBrush(on
                ? System.Windows.Media.Color.FromRgb(0x2A, 0x35, 0x50)
                : System.Windows.Media.Color.FromRgb(0x2A, 0x2A, 0x2A));
            pill.BorderBrush = new SolidColorBrush(on
                ? System.Windows.Media.Color.FromRgb(0x7A, 0xA2, 0xF7)
                : System.Windows.Media.Color.FromRgb(0x3A, 0x3A, 0x3A));
            ((TextBlock)pill.Child).Foreground = new SolidColorBrush(on
                ? System.Windows.Media.Color.FromRgb(0x9D, 0xB8, 0xFF)
                : System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC));
        }
    }

    /// <summary>Moves the pill selection by <paramref name="delta"/>, wrapping around.</summary>
    private void MovePill(int delta)
    {
        if (_pills.Count == 0) return;
        int n = _pills.Count;
        _pillIndex = ((_pillIndex + delta) % n + n) % n;
        HighlightPill();
    }

    private void OnPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (!_pillMode) return;
        e.Handled = true;
        MovePill(e.Delta > 0 ? -1 : 1);
    }

    /// <summary>
    /// Commits the chosen action: reveals the question input, freezes the bubble
    /// in place, and draws the capture bounding box around the original cursor.
    /// </summary>
    private void SelectPill(int index)
    {
        _pillIndex = index;
        _mode = PillDefs[index].Mode;
        _pillMode = false;
        HighlightPill();

        PillPanel.Visibility = Visibility.Collapsed;
        AskPanel.Visibility = Visibility.Visible;
        HeaderText.Text = "Ask about your selection";
        ModeBadgeText.Text = PillDefs[index].Label;
        Hint.Text = _mode == AskMode.Search
            ? "Type a search, Enter to search  \u00b7  Esc to close"
            : "Type your question, Enter to ask  \u00b7  Esc to close";

        // Freeze the bubble where it is so the user can type without it drifting.
        _followTimer.Stop();
        ShowBoundingBox();

        Input.Focus();
        Keyboard.Focus(Input);
    }

    /// <summary>Draws a glassy dashed rectangle showing the captured region.</summary>
    private void ShowBoundingBox()
    {
        try
        {
            int box = Glint.Core.SettingsStore.Current.CaptureBoxSize;
            if (box <= 0) box = 800;
            double half = box / 2.0;
            var topLeft = Layer.PointFromScreen(
                new System.Windows.Point(_context.CursorX - half, _context.CursorY - half));

            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = box,
                Height = box,
                RadiusX = 12,
                RadiusY = 12,
                StrokeThickness = 2,
                Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xAA, 0x7A, 0xA2, 0xF7)),
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x10, 0x7A, 0xA2, 0xF7)),
                IsHitTestVisible = false,
                Opacity = 0,
            };
            Canvas.SetLeft(rect, topLeft.X);
            Canvas.SetTop(rect, topLeft.Y);
            FxCanvas.Children.Add(rect);
            rect.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }
        catch
        {
            // Decorative only; ignore if the visual isn't ready.
        }
    }

    /// <summary>Scales + fades the bubble in from the cursor, with a soft overshoot.</summary>
    private void PlayEntrance()
    {
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
        var dur = TimeSpan.FromMilliseconds(220);

        Bubble.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));

        var scale = new DoubleAnimation(0.85, 1.0, dur) { EasingFunction = ease };
        BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);

        BubbleTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, dur) { EasingFunction = ease });
    }

    /// <summary>
    /// Spawns an expanding glass ring plus a few sparkles at the trigger point,
    /// then removes them. Purely decorative and non-hit-testable.
    /// </summary>
    private void SpawnCursorFx()
    {
        var (sx, sy) = Capture.ScreenCapture.GetCursorPosition();
        System.Windows.Point c;
        try { c = Layer.PointFromScreen(new System.Windows.Point(sx, sy)); }
        catch { return; }

        // Expanding ring.
        var ring = new Ellipse
        {
            Width = 34,
            Height = 34,
            Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 2,
            IsHitTestVisible = false,
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
        };
        var ringScale = new ScaleTransform(0.4, 0.4);
        ring.RenderTransform = ringScale;
        PlaceCentered(ring, c, 34);
        FxCanvas.Children.Add(ring);

        var ringDur = TimeSpan.FromMilliseconds(520);
        var ringEase = new CubicEase { EasingMode = EasingMode.EaseOut };
        ringScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.4, 2.4, ringDur) { EasingFunction = ringEase });
        ringScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.4, 2.4, ringDur) { EasingFunction = ringEase });
        var ringFade = new DoubleAnimation(0.85, 0, ringDur) { EasingFunction = ringEase };
        ringFade.Completed += (_, _) => FxCanvas.Children.Remove(ring);
        ring.BeginAnimation(OpacityProperty, ringFade);

        // Soft glow pulse.
        var glow = new Ellipse
        {
            Width = 70,
            Height = 70,
            IsHitTestVisible = false,
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            Fill = new RadialGradientBrush(
                System.Windows.Media.Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF),
                System.Windows.Media.Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF)),
        };
        var glowScale = new ScaleTransform(0.5, 0.5);
        glow.RenderTransform = glowScale;
        PlaceCentered(glow, c, 70);
        FxCanvas.Children.Insert(0, glow);
        var glowDur = TimeSpan.FromMilliseconds(420);
        glowScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.5, 1.4, glowDur));
        glowScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.5, 1.4, glowDur));
        var glowFade = new DoubleAnimation(0.9, 0, glowDur);
        glowFade.Completed += (_, _) => FxCanvas.Children.Remove(glow);
        glow.BeginAnimation(OpacityProperty, glowFade);

        // Sparkles flying outward.
        const int count = 6;
        var rnd = new Random();
        for (int i = 0; i < count; i++)
        {
            double angle = (Math.PI * 2 * i / count) + rnd.NextDouble() * 0.5;
            double dist = 26 + rnd.NextDouble() * 18;
            SpawnSparkle(c, angle, dist);
        }
    }

    /// <summary>Adds a small rotating diamond sparkle that flies out and fades.</summary>
    private void SpawnSparkle(System.Windows.Point center, double angle, double distance)
    {
        var size = 6.0;
        var spark = new System.Windows.Shapes.Rectangle
        {
            Width = size,
            Height = size,
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)),
            IsHitTestVisible = false,
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
        };
        var move = new TranslateTransform(0, 0);
        var rot = new RotateTransform(45);
        var grp = new TransformGroup();
        grp.Children.Add(rot);
        grp.Children.Add(move);
        spark.RenderTransform = grp;
        PlaceCentered(spark, center, size);
        FxCanvas.Children.Add(spark);

        var dur = TimeSpan.FromMilliseconds(460);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        move.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, Math.Cos(angle) * distance, dur) { EasingFunction = ease });
        move.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, Math.Sin(angle) * distance, dur) { EasingFunction = ease });
        rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(45, 225, dur));
        var fade = new DoubleAnimation(1, 0, dur) { EasingFunction = ease };
        fade.Completed += (_, _) => FxCanvas.Children.Remove(spark);
        spark.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Positions an element so its center sits at <paramref name="p"/> in the layer.</summary>
    private static void PlaceCentered(FrameworkElement el, System.Windows.Point p, double size)
    {
        Canvas.SetLeft(el, p.X - size / 2);
        Canvas.SetTop(el, p.Y - size / 2);
    }

    /// <summary>
    /// Reliably brings this window to the foreground and gives it keyboard focus,
    /// working around Windows' SetForegroundWindow restrictions for background apps.
    /// </summary>
    private void ForceForeground()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var foreground = GetForegroundWindow();
        uint foreThread = GetWindowThreadProcessId(foreground, out _);
        uint thisThread = GetCurrentThreadId();

        bool attached = false;
        try
        {
            if (foreThread != 0 && foreThread != thisThread)
                attached = AttachThreadInput(thisThread, foreThread, true);

            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            SetFocus(hwnd);
        }
        catch
        {
            // Best-effort; focus may still land via Activate().
        }
        finally
        {
            if (attached)
                AttachThreadInput(thisThread, foreThread, false);
        }
    }

    /// <summary>Size and place the window to span every monitor (physical px).</summary>
    private void CoverVirtualDesktop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        SetWindowPos(hwnd, IntPtr.Zero, vx, vy, vw, vh, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>
    /// Place the bubble just below-right of the live cursor. Uses
    /// PointFromScreen so physical cursor pixels map correctly into the
    /// overlay's DPI-aware coordinate space.
    /// </summary>
    private void PositionBubble()
    {
        var (x, y) = Capture.ScreenCapture.GetCursorPosition();
        try
        {
            var p = Layer.PointFromScreen(new System.Windows.Point(x, y));
            Canvas.SetLeft(Bubble, p.X + FollowOffset);
            Canvas.SetTop(Bubble, p.Y + FollowOffset);
        }
        catch
        {
            // Visual not connected yet; ignore this tick.
        }
    }

    private void Root_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Click anywhere outside the bubble dismisses it.
        Dismiss();
    }

    private void Bubble_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Clicks on the bubble itself must NOT dismiss it.
        e.Handled = true;
        Input.Focus();
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Dismiss();
            return;
        }

        // While choosing an action, arrows move the selection and Enter commits.
        if (_pillMode)
        {
            if (e.Key == Key.Left || e.Key == Key.Up)
            {
                e.Handled = true;
                MovePill(-1);
            }
            else if (e.Key == Key.Right || e.Key == Key.Down)
            {
                e.Handled = true;
                MovePill(1);
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                SelectPill(_pillIndex);
            }
        }
    }

    private void Dismiss()
    {
        _cts?.Cancel();
        Close();
    }

    private async void Input_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Dismiss();
            return;
        }

        if (e.Key == Key.Enter && !_busy)
        {
            e.Handled = true;
            var query = Input.Text.Trim();
            // Search needs terms; Summarize/Explain can run on the screen alone.
            if (query.Length == 0 && _mode == AskMode.Search) return;
            await AskAsync(query);
        }
    }

    private async System.Threading.Tasks.Task AskAsync(string query)
    {
        _busy = true;
        _cts = new CancellationTokenSource();

        // Search mode shows web result cards alongside the AI summary.
        if (_mode == AskMode.Search)
        {
            ShowMockResults(query);
            Hint.Text = "Searching\u2026";
        }
        else
        {
            Hint.Text = "Thinking\u2026";
        }

        AnswerBox.Visibility = Visibility.Visible;
        Answer.Text = "\u2026";

        try
        {
            var prompt = BuildModePrompt(query);
            var result = await _provider.AskAsync(prompt, _context, _cts.Token);
            Answer.Text = result.Text;
            Answer.Foreground = result.IsError
                ? System.Windows.Media.Brushes.IndianRed
                : new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0));
            Hint.Text = "Enter to ask again  \u00b7  Esc to close";
        }
        catch (OperationCanceledException)
        {
            // Closed/cancelled; nothing to show.
        }
        catch (Exception ex)
        {
            Answer.Text = "Error: " + ex.Message;
            Answer.Foreground = System.Windows.Media.Brushes.IndianRed;
            Hint.Text = "Esc to close";
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Wraps the user's text with intent based on the chosen pill.</summary>
    private string BuildModePrompt(string query)
    {
        return _mode switch
        {
            AskMode.Summarize => string.IsNullOrEmpty(query)
                ? "Summarize the content shown on screen concisely."
                : $"Summarize the content shown on screen, focusing on: {query}",
            AskMode.Explain => string.IsNullOrEmpty(query)
                ? "Explain the content shown on screen in simple, clear terms."
                : $"Explain the content shown on screen, specifically: {query}",
            AskMode.Search => $"The user is searching the web for: {query}. " +
                "Using the screen context and your knowledge, give a concise, helpful answer.",
            _ => query,
        };
    }

    /// <summary>
    /// Renders a few mocked web-result cards (thumbnail + title link + snippet)
    /// so Search mode shows richer info than a single block of text.
    /// </summary>
    private void ShowMockResults(string query)
    {
        Results.Children.Clear();
        var q = System.Uri.EscapeDataString(query);
        var cards = new[]
        {
            ($"{query} \u2014 overview", "en.wikipedia.org",
                "A concise overview covering the key facts, background, and context to get you started.",
                $"https://en.wikipedia.org/w/index.php?search={q}"),
            ($"Understanding {query}", "guide.example.com",
                "A practical guide with examples, common pitfalls, and step-by-step explanations.",
                $"https://www.google.com/search?q={q}"),
            ($"Latest on {query}", "news.google.com",
                "Recent articles and updates from around the web, summarized for quick reading.",
                $"https://news.google.com/search?q={q}"),
        };
        foreach (var (title, host, snippet, url) in cards)
            Results.Children.Add(BuildResultCard(title, host, snippet, url));
        ResultsScroll.Visibility = Visibility.Visible;
    }

    /// <summary>Builds one clickable web-result card.</summary>
    private Border BuildResultCard(string title, string host, string snippet, string url)
    {
        var thumb = new Border
        {
            Width = 52,
            Height = 52,
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 10, 0),
            Background = new LinearGradientBrush(
                System.Windows.Media.Color.FromRgb(0x4A, 0x5A, 0x8A),
                System.Windows.Media.Color.FromRgb(0x2A, 0x33, 0x50), 45),
        };
        DockPanel.SetDock(thumb, Dock.Left);

        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9D, 0xB8, 0xFF)),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock
        {
            Text = host,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88)),
            FontSize = 11,
            Margin = new Thickness(0, 1, 0, 3),
        });
        text.Children.Add(new TextBlock
        {
            Text = snippet,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0xC0, 0xC0)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 40,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var dp = new DockPanel();
        dp.Children.Add(thumb);
        dp.Children.Add(text);

        var card = new Border
        {
            Child = dp,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x25, 0x25, 0x25)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x3A, 0x3A)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        card.MouseLeftButtonUp += (_, e) => { e.Handled = true; OpenUrl(url); };
        return card;
    }

    /// <summary>Opens a URL in the user's default browser.</summary>
    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Best-effort; ignore if no browser is available.
        }
    }

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOMOVE = 0x0002;

    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);
}
