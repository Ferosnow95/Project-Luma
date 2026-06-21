using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Glint.Core;

namespace Glint.Providers;

/// <summary>
/// Sends the captured screenshot + the user's question to a locally-running
/// Ollama server (vision models like "llava" or "llama3.2-vision").
///
/// This needs no API key, no account, and has no quota or region limits — it
/// runs entirely on the user's machine. Install from https://ollama.com and
/// pull a vision model, e.g. <c>ollama pull llava</c>.
/// </summary>
public sealed class OllamaProvider : IResponseProvider
{
    private static readonly HttpClient Http = new()
    {
        // Local inference can be slow on CPU; give it room.
        Timeout = TimeSpan.FromMinutes(5),
    };

    /// <summary>Local vision model, read from settings (e.g. "llava").</summary>
    public string Model => string.IsNullOrWhiteSpace(SettingsStore.Current.OllamaModel)
        ? "llava"
        : SettingsStore.Current.OllamaModel;

    private static string Endpoint => string.IsNullOrWhiteSpace(SettingsStore.Current.OllamaEndpoint)
        ? "http://localhost:11434"
        : SettingsStore.Current.OllamaEndpoint.TrimEnd('/');

    public string Name => $"Ollama ({Model})";

    public async Task<ProviderResult> AskAsync(string query, CursorContext context, CancellationToken ct)
    {
        try
        {
            var url = $"{Endpoint}/api/chat";
            var payload = BuildPayload(query, context);

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };

            using var resp = await Http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                return ProviderResult.Error($"Ollama error {(int)resp.StatusCode}: {ExtractError(body)}");

            var answer = ExtractAnswer(body);
            return string.IsNullOrWhiteSpace(answer)
                ? ProviderResult.Error("Ollama returned an empty response.")
                : ProviderResult.Ok(answer);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return ProviderResult.Error(
                $"Can't reach Ollama at {Endpoint}.\n" +
                "Install it from ollama.com, then run:  ollama pull " + Model);
        }
        catch (Exception ex)
        {
            return ProviderResult.Error("Request failed: " + ex.Message);
        }
    }

    private string BuildPayload(string query, CursorContext context)
    {
        var sb = new StringBuilder();
        sb.Append(query);
        if (!string.IsNullOrWhiteSpace(context.ElementText))
        {
            sb.Append("\n\nText under the cursor:\n");
            sb.Append(context.ElementText);
        }

        // Ollama takes images as a base64 array on the message (no data: prefix).
        var images = new List<string>();
        if (context.ScreenshotPng is { Length: > 0 } png)
            images.Add(Convert.ToBase64String(png));

        var requestBody = new
        {
            model = Model,
            stream = false,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content =
                        "You are a screen-aware assistant. The user pressed a hotkey to ask " +
                        "about whatever is near their mouse cursor. You are given a screenshot " +
                        "of that screen region plus their question. Answer concisely and " +
                        "specifically about what is shown. If the screenshot is unclear, say so.",
                },
                new
                {
                    role = "user",
                    content = sb.ToString(),
                    images = images.ToArray(),
                },
            },
        };

        return JsonSerializer.Serialize(requestBody);
    }

    private static string ExtractAnswer(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("message", out var msg) &&
            msg.TryGetProperty("content", out var content))
        {
            return content.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static string ExtractError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err))
                return err.GetString() ?? json;
        }
        catch
        {
            // fall through
        }
        return json.Length > 300 ? json[..300] : json;
    }
}
