using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Glint.Core;
using Glint.Providers;
using Glint.Settings;
using Application = System.Windows.Application;

namespace Glint;

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
    private readonly OllamaProvider _ollama = new();

    private ToolStripMenuItem? _mockItem;
    private ToolStripMenuItem? _geminiItem;
    private ToolStripMenuItem? _openAiItem;
    private ToolStripMenuItem? _ollamaItem;
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

        Log.Info($"Glint starting. Log file: {Log.FilePath}");
        SettingsStore.Load();

        // Run headless: closing windows must not exit the app.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Use the provider the user last selected (persisted in settings).
        _controller = new CursorChatController(ProviderFromId(SettingsStore.Current.ActiveProvider));
        _controller.Start();

        SetupTray();
        Log.Info("Glint ready. Press / to summon the bubble.");
    }

    private void SetupTray()
    {
        var menu = new ContextMenuStrip();

        _mockItem = new ToolStripMenuItem("Provider: Mock");
        _geminiItem = new ToolStripMenuItem("Provider: Gemini (free)");
        _ollamaItem = new ToolStripMenuItem("Provider: Ollama (local, free)");
        _openAiItem = new ToolStripMenuItem("Provider: OpenAI");
        _googleItem = new ToolStripMenuItem("Provider: Google (browser)");

        _mockItem.Click += (_, _) => SelectProvider(_mock);
        _geminiItem.Click += (_, _) => SelectProvider(_gemini);
        _ollamaItem.Click += (_, _) => SelectProvider(_ollama);
        _openAiItem.Click += (_, _) => SelectProvider(_openAi);
        _googleItem.Click += (_, _) => SelectProvider(_google);

        SyncProviderChecks();

        menu.Items.Add(_mockItem);
        menu.Items.Add(_geminiItem);
        menu.Items.Add(_ollamaItem);
        menu.Items.Add(_openAiItem);
        menu.Items.Add(_googleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings\u2026", null, (_, _) => OpenSettings());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "Glint \u2014 press / to ask",
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => OpenSettings();
    }

    /// <summary>Switches the active provider and updates the tray checkmarks.</summary>
    private void SelectProvider(IResponseProvider provider)
    {
        if (_controller is null) return;
        _controller.Provider = provider;
        SettingsStore.Current.ActiveProvider = IdFromProvider(provider);
        SettingsStore.Save();
        SyncProviderChecks();
    }

    /// <summary>Maps a persisted provider id to its provider instance.</summary>
    private IResponseProvider ProviderFromId(string? id) => id switch
    {
        "mock" => _mock,
        "gemini" => _gemini,
        "ollama" => _ollama,
        "openai" => _openAi,
        _ => _google,
    };

    /// <summary>Maps a provider instance to its persisted id.</summary>
    private string IdFromProvider(IResponseProvider provider) =>
        provider == _mock ? "mock" :
        provider == _gemini ? "gemini" :
        provider == _ollama ? "ollama" :
        provider == _openAi ? "openai" :
        "google";

    /// <summary>Ticks the menu item matching the controller's current provider.</summary>
    private void SyncProviderChecks()
    {
        var p = _controller?.Provider;
        if (_mockItem is not null) _mockItem.Checked = p == _mock;
        if (_geminiItem is not null) _geminiItem.Checked = p == _gemini;
        if (_ollamaItem is not null) _ollamaItem.Checked = p == _ollama;
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
        // The settings window persists ActiveProvider; honor the user's choice.
        SelectProvider(ProviderFromId(SettingsStore.Current.ActiveProvider));
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

