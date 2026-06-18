using System;
using System.Windows;
using System.Windows.Controls;
using SlashCursor.Core;
using ComboBox = System.Windows.Controls.ComboBox;

namespace SlashCursor.Settings;

/// <summary>
/// Lets the user paste their OpenAI API key (stored DPAPI-encrypted), choose
/// the model, and set the screenshot capture size.
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>Raised after the user saves; lets the app re-select the best provider.</summary>
    public event Action? Saved;

    public SettingsWindow()
    {
        InitializeComponent();
        LoadCurrent();
    }

    private void LoadCurrent()
    {
        var s = SettingsStore.Current;

        // Gemini: never display the stored key; just indicate its presence.
        var hasGemStored = !string.IsNullOrWhiteSpace(s.GeminiKeyProtected);
        var hasGemEnv = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY"))
                        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GOOGLE_API_KEY"));
        GeminiKeyStatus.Text = hasGemStored
            ? "A Gemini key is saved (encrypted). Leave blank to keep it, or paste a new one to replace."
            : hasGemEnv
                ? "Using GEMINI_API_KEY from your environment. Paste a key here to store one in-app instead."
                : "No Gemini key set. Get a free one at aistudio.google.com/apikey, paste it, and click Save.";
        SelectByTag(GeminiModelBox, s.GeminiModel);

        // OpenAI: never display the stored key; just indicate its presence.
        var hasStored = !string.IsNullOrWhiteSpace(s.OpenAiKeyProtected);
        var hasEnv = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        KeyStatus.Text = hasStored
            ? "A key is saved (encrypted). Leave blank to keep it, or paste a new one to replace."
            : hasEnv
                ? "Using OPENAI_API_KEY from your environment. Paste a key here to store one in-app instead."
                : "No key set yet. Paste your OpenAI key (sk-...) and click Save.";

        SelectByTag(ModelBox, s.Model);
        LoadOllamaModel(s.OllamaModel);
        SelectByTag(CaptureBox, s.CaptureWholeScreen ? "whole" : s.CaptureBoxSize.ToString());
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Store keys only if the user typed one (blank = keep existing).
        var gemTyped = GeminiKeyBox.Password;
        if (!string.IsNullOrWhiteSpace(gemTyped))
            SettingsStore.SetGeminiKey(gemTyped);

        if (SelectedTag(GeminiModelBox) is { } gemModel)
            SettingsStore.Current.GeminiModel = gemModel;

        var typed = KeyBox.Password;
        if (!string.IsNullOrWhiteSpace(typed))
            SettingsStore.SetOpenAiKey(typed);

        if (SelectedTag(ModelBox) is { } model)
            SettingsStore.Current.Model = model;

        var ollamaModel = OllamaModelText();
        if (!string.IsNullOrWhiteSpace(ollamaModel))
            SettingsStore.Current.OllamaModel = ollamaModel;

        if (SelectedTag(CaptureBox) is { } cap)
        {
            if (cap == "whole")
            {
                SettingsStore.Current.CaptureWholeScreen = true;
            }
            else if (int.TryParse(cap, out var size))
            {
                SettingsStore.Current.CaptureWholeScreen = false;
                SettingsStore.Current.CaptureBoxSize = size;
            }
        }

        SettingsStore.Save();

        KeyBox.Clear();
        GeminiKeyBox.Clear();
        SaveStatus.Text = "Saved.";
        LoadCurrent();
        Saved?.Invoke();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Selects the matching item for the editable Ollama combo, or shows the raw value.</summary>
    private void LoadOllamaModel(string? model)
    {
        foreach (ComboBoxItem item in OllamaModelBox.Items)
        {
            if ((item.Tag as string) == model)
            {
                OllamaModelBox.SelectedItem = item;
                return;
            }
        }
        OllamaModelBox.Text = model ?? "llava";
    }

    /// <summary>Reads the Ollama model from the editable combo (selected tag or typed text).</summary>
    private string? OllamaModelText()
    {
        if (OllamaModelBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            return tag;
        return string.IsNullOrWhiteSpace(OllamaModelBox.Text) ? null : OllamaModelBox.Text.Trim();
    }

    private static void SelectByTag(ComboBox box, string? tag)
    {
        foreach (ComboBoxItem item in box.Items)
        {
            if ((item.Tag as string) == tag)
            {
                box.SelectedItem = item;
                return;
            }
        }
        if (box.Items.Count > 0) box.SelectedIndex = 0;
    }

    private static string? SelectedTag(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
