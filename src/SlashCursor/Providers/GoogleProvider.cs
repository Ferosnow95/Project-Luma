using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SlashCursor.Core;

namespace SlashCursor.Providers;

/// <summary>
/// Opens a Google search in the user's default browser. This needs no API key,
/// no billing, and has no quota — it just launches a search URL.
///
/// Modes (see <see cref="AppSettings.GoogleSearchMode"/>):
///   "web"    - google.com/search?q=...
///   "images" - google.com/search?tbm=isch&amp;q=...
///
/// For "what is this on my screen?", the captured screenshot is saved to a temp
/// PNG and Google Lens is opened so the user can drag the file in (there is no
/// free API for reverse image search, so this is the practical free path).
/// </summary>
public sealed class GoogleProvider : IResponseProvider
{
    public string Name => "Google (browser)";

    private static string Mode => string.Equals(SettingsStore.Current.GoogleSearchMode, "images",
        StringComparison.OrdinalIgnoreCase) ? "images" : "web";

    public Task<ProviderResult> AskAsync(string query, CursorContext context, CancellationToken ct)
    {
        try
        {
            // Combine the typed question with any text read under the cursor.
            var terms = query?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(context.ElementText))
            {
                terms = string.IsNullOrWhiteSpace(terms)
                    ? context.ElementText.Trim()
                    : terms + " " + context.ElementText.Trim();
            }

            var savedShot = SaveScreenshot(context);
            var msg = new StringBuilder();

            if (string.IsNullOrWhiteSpace(terms))
            {
                // No words to search: go straight to reverse-image search (Lens).
                OpenUrl("https://lens.google.com/");
                msg.AppendLine("Opened Google Lens in your browser.");
                if (savedShot is not null)
                {
                    msg.AppendLine();
                    msg.AppendLine("Drag this saved screenshot into Lens to find what it is:");
                    msg.Append(savedShot);
                }
                else
                {
                    msg.Append("Type a question first, or capture a screen region to search by image.");
                }
                return Task.FromResult(ProviderResult.Ok(msg.ToString()));
            }

            var url = Mode == "images"
                ? "https://www.google.com/search?tbm=isch&q=" + Uri.EscapeDataString(terms)
                : "https://www.google.com/search?q=" + Uri.EscapeDataString(terms);

            OpenUrl(url);

            msg.AppendLine(Mode == "images"
                ? $"Searched Google Images for: {terms}"
                : $"Searched Google for: {terms}");
            msg.Append("(Opened in your browser.)");

            if (savedShot is not null)
            {
                msg.AppendLine();
                msg.AppendLine();
                msg.AppendLine("To reverse-search what's on screen, open lens.google.com and drag in:");
                msg.Append(savedShot);
            }

            return Task.FromResult(ProviderResult.Ok(msg.ToString()));
        }
        catch (Exception ex)
        {
            Log.Error("Google search failed", ex);
            return Task.FromResult(ProviderResult.Error("Couldn't open the search: " + ex.Message));
        }
    }

    /// <summary>Writes the screenshot to %TEMP%\SlashCursor\search-*.png; returns the path or null.</summary>
    private static string? SaveScreenshot(CursorContext context)
    {
        if (context.ScreenshotPng is not { Length: > 0 } png)
            return null;

        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "SlashCursor");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"search-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            File.WriteAllBytes(path, png);
            return path;
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save screenshot for Google search", ex);
            return null;
        }
    }

    private static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
