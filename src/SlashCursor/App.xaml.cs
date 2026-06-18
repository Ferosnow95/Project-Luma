using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using SlashCursor.Core;
using SlashCursor.Providers;
using SlashCursor.Settings;
using Application = System.Windows.Application;

namespace SlashCursor;

/// <summary>
/// Interaction logic for App.xaml. Runs headless in the tray.
/// </summary>
public partial class App : Application
{
    private NotifyIcon? _tray;
    private CursorChatController? _controller;
    private SettingsWindow? _settingsWindow;

    private readonly MockProvider _mock = new();
    private readonly GoogleProvider _google = new();
    private readonly OpenAiProvider _openAi = new();
    private readonly GeminiProvider _gemini = new();

    private ToolStripMenuItem? _mockItem;
    private ToolStripMenuItem? _geminiItem;
    private ToolStripMenuItem? _openAiItem;
    private ToolStripMenuItem? _googleItem;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Capture any crash so headless failures are diagnosable.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled dispatcher exception", args.Exception);
            args.Handled = true; // keep the app alive in the tray
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled domain exception", args.ExceptionObject as Exception);

        Log.Info($"SlashCursor starting. Log file: {Log.FilePath}");
        SettingsStore.Load();

        // Run headless: closing windows must not exit the app.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Prefer Gemini (free tier) when a key is available, then OpenAI, else Mock.
        IResponseProvider initial =
            SettingsStore.HasGeminiKey() ? _gemini :
            SettingsStore.HasOpenAiKey() ? _openAi :
            _mock;
        _controller = new CursorChatController(initial);
        _controller.Start();

        SetupTray();
        Log.Info("SlashCursor ready. Press / to summon the bubble.");
    }

    private void SetupTray()
    {
        var menu = new ContextMenuStrip();

        _mockItem = new ToolStripMenuItem("Provider: Mock");
        _geminiItem = new ToolStripMenuItem("Provider: Gemini (free)");
        _openAiItem = new ToolStripMenuItem("Provider: OpenAI");
        _googleItem = new ToolStripMenuItem("Provider: Google (stub)");

        _mockItem.Click += (_, _) => SelectProvider(_mock);
        _geminiItem.Click += (_, _) => SelectProvider(_gemini);
        _openAiItem.Click += (_, _) => SelectProvider(_openAi);
        _googleItem.Click += (_, _) => SelectProvider(_google);

        SyncProviderChecks();

        menu.Items.Add(_mockItem);
        menu.Items.Add(_geminiItem);
        menu.Items.Add(_openAiItem);
        menu.Items.Add(_googleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings\u2026", null, (_, _) => OpenSettings());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "SlashCursor \u2014 press / to ask",
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => OpenSettings();
    }

    /// <summary>Switches the active provider and updates the tray checkmarks.</summary>
    private void SelectProvider(IResponseProvider provider)
    {
        if (_controller is null) return;
        _controller.Provider = provider;
        SyncProviderChecks();
    }

    /// <summary>Ticks the menu item matching the controller's current provider.</summary>
    private void SyncProviderChecks()
    {
        var p = _controller?.Provider;
        if (_mockItem is not null) _mockItem.Checked = p == _mock;
        if (_geminiItem is not null) _geminiItem.Checked = p == _gemini;
        if (_openAiItem is not null) _openAiItem.Checked = p == _openAi;
        if (_googleItem is not null) _googleItem.Checked = p == _google;
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Saved += OnSettingsSaved;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>
    /// After settings are saved, switch to the best newly-available provider so
    /// the user doesn't keep hitting a previously-selected provider (e.g. a
    /// quota-exhausted OpenAI) after pasting a fresh Gemini key.
    /// </summary>
    private void OnSettingsSaved()
    {
        var current = _controller?.Provider;
        // Only auto-switch away from non-cloud or unconfigured providers.
        var onPlaceholder = current == _mock || current == _google;
        var onOpenAiNoKey = current == _openAi && !SettingsStore.HasOpenAiKey();

        if (SettingsStore.HasGeminiKey() && (onPlaceholder || onOpenAiNoKey || current == _openAi))
            SelectProvider(_gemini);
        else if (SettingsStore.HasOpenAiKey() && onPlaceholder)
            SelectProvider(_openAi);
        else
            SyncProviderChecks();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _controller?.Dispose();
        base.OnExit(e);
    }
}

