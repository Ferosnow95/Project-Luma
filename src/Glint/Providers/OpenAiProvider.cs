using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Glint.Core;

namespace Glint.Providers;

/// <summary>
/// Sends the captured screenshot + the user's question to OpenAI's
/// chat completions API (vision-capable models) and returns the answer.
///
/// The API key comes from <see cref="SettingsStore"/> (DPAPI-encrypted on disk,
/// or the OPENAI_API_KEY environment variable) so it never lives in source.
/// </summary>
public sealed class OpenAiProvider : IResponseProvider
{
    private const string Endpoint = "https://api.openai.com/v1/chat/completions";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    /// <summary>Vision-capable model id, read from settings (e.g. "gpt-4o-mini").</summary>
    public string Model => string.IsNullOrWhiteSpace(SettingsStore.Current.Model)
        ? "gpt-4o-mini"
        : SettingsStore.Current.Model;

    public string Name => $"OpenAI ({Model})";

    public async Task<ProviderResult> AskAsync(string query, CursorContext context, CancellationToken ct)
    {
        var apiKey = SettingsStore.GetOpenAiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return ProviderResult.Error(
                "No OpenAI API key set.\n" +
                "Open the tray menu \u2192 Settings\u2026 and paste your key.");
        }

        try
        {
            var payload = BuildPayload(query, context);
            using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var resp = await Http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                return ProviderResult.Error($"OpenAI error {(int)resp.StatusCode}: {ExtractError(body)}");

            var answer = ExtractAnswer(body);
            return string.IsNullOrWhiteSpace(answer)
                ? ProviderResult.Error("OpenAI returned an empty response.")
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

    private string BuildPayload(string query, CursorContext context)
    {
        // Build the user message content: the question, optional element text,
        // and the screenshot (as a base64 data URL) when available.
        var userContent = new List<object>();

        var sb = new StringBuilder();
        sb.Append(query);
        if (!string.IsNullOrWhiteSpace(context.ElementText))
        {
            sb.Append("\n\nText under the cursor:\n");
            sb.Append(context.ElementText);
        }
        userContent.Add(new { type = "text", text = sb.ToString() });

        if (context.ScreenshotPng is { Length: > 0 } png)
        {
            var dataUrl = "data:image/png;base64," + Convert.ToBase64String(png);
            userContent.Add(new
            {
                type = "image_url",
                image_url = new { url = dataUrl },
            });
        }

        var requestBody = new
        {
            model = Model,
            max_tokens = 600,
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
                new { role = "user", content = userContent },
            },
        };

        return JsonSerializer.Serialize(requestBody);
    }

    private static string ExtractAnswer(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
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
