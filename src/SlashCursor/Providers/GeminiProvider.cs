using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SlashCursor.Core;

namespace SlashCursor.Providers;

/// <summary>
/// Sends the captured screenshot + the user's question to Google's Gemini API
/// (generateContent, vision-capable models) and returns the answer.
///
/// Gemini has a free tier that needs no billing, which makes it a good default.
/// Get a key at https://aistudio.google.com/apikey.
///
/// The API key comes from <see cref="SettingsStore"/> (DPAPI-encrypted on disk,
/// or the GEMINI_API_KEY / GOOGLE_API_KEY environment variable) so it never
/// lives in source.
/// </summary>
public sealed class GeminiProvider : IResponseProvider
{
    private const string EndpointBase = "https://generativelanguage.googleapis.com/v1beta/models/";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    /// <summary>Vision-capable model id, read from settings (e.g. "gemini-2.0-flash").</summary>
    public string Model => string.IsNullOrWhiteSpace(SettingsStore.Current.GeminiModel)
        ? "gemini-2.0-flash"
        : SettingsStore.Current.GeminiModel;

    public string Name => $"Gemini ({Model})";

    public async Task<ProviderResult> AskAsync(string query, CursorContext context, CancellationToken ct)
    {
        var apiKey = SettingsStore.GetGeminiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return ProviderResult.Error(
                "No Gemini API key set.\n" +
                "Get a free key at aistudio.google.com/apikey, then open the " +
                "tray menu \u2192 Settings\u2026 and paste it.");
        }

        try
        {
            var url = $"{EndpointBase}{Uri.EscapeDataString(Model)}:generateContent";
            var payload = BuildPayload(query, context);

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            // Send the key in a header rather than the query string so it stays out of logs.
            req.Headers.Add("x-goog-api-key", apiKey);

            using var resp = await Http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                return ProviderResult.Error($"Gemini error {(int)resp.StatusCode}: {ExtractError(body)}");

            var answer = ExtractAnswer(body);
            return string.IsNullOrWhiteSpace(answer)
                ? ProviderResult.Error("Gemini returned an empty response.")
                : ProviderResult.Ok(answer);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ProviderResult.Error("Request failed: " + ex.Message);
        }
    }

    private static string BuildPayload(string query, CursorContext context)
    {
        // The user message: the question, optional element text, and the
        // screenshot as inline base64 data when available.
        var parts = new List<object>();

        var sb = new StringBuilder();
        sb.Append(query);
        if (!string.IsNullOrWhiteSpace(context.ElementText))
        {
            sb.Append("\n\nText under the cursor:\n");
            sb.Append(context.ElementText);
        }
        parts.Add(new { text = sb.ToString() });

        if (context.ScreenshotPng is { Length: > 0 } png)
        {
            parts.Add(new
            {
                inline_data = new
                {
                    mime_type = "image/png",
                    data = Convert.ToBase64String(png),
                },
            });
        }

        var requestBody = new
        {
            systemInstruction = new
            {
                parts = new[]
                {
                    new
                    {
                        text =
                            "You are a screen-aware assistant. The user pressed a hotkey to ask " +
                            "about whatever is near their mouse cursor. You are given a screenshot " +
                            "of that screen region plus their question. Answer concisely and " +
                            "specifically about what is shown. If the screenshot is unclear, say so.",
                    },
                },
            },
            contents = new object[]
            {
                new { role = "user", parts },
            },
            generationConfig = new { maxOutputTokens = 800 },
        };

        return JsonSerializer.Serialize(requestBody);
    }

    private static string ExtractAnswer(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) ||
            candidates.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        var content = candidates[0];
        if (content.TryGetProperty("content", out var c) &&
            c.TryGetProperty("parts", out var parts))
        {
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var t))
                    sb.Append(t.GetString());
            }
        }
        return sb.ToString();
    }

    private static string ExtractError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err) &&
                err.TryGetProperty("message", out var msg))
            {
                return msg.GetString() ?? json;
            }
        }
        catch
        {
            // fall through
        }
        return json.Length > 300 ? json[..300] : json;
    }
}
