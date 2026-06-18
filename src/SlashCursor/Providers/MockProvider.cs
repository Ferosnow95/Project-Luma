using System;
using System.Threading;
using System.Threading.Tasks;

namespace SlashCursor.Providers;

/// <summary>
/// A no-cost, no-key provider that returns canned responses.
/// Lets the whole trigger -> capture -> bubble loop be developed and tested
/// without any AI API. Simulates latency so the UI feels realistic.
/// </summary>
public sealed class MockProvider : IResponseProvider
{
    public string Name => "Mock";

    public async Task<ProviderResult> AskAsync(string query, CursorContext context, CancellationToken ct)
    {
        // Pretend we're calling a network service.
        await Task.Delay(500, ct);

        var hasShot = context.ScreenshotPng is { Length: > 0 };
        var shotInfo = hasShot
            ? $"a {context.ScreenshotPng!.Length / 1024} KB screenshot near ({context.CursorX}, {context.CursorY})"
            : "no screenshot";

        var elementInfo = string.IsNullOrWhiteSpace(context.ElementText)
            ? "no element text"
            : $"element text: \"{Trim(context.ElementText!, 80)}\"";

        var text =
            $"[MOCK ANSWER]\n" +
            $"You asked: \"{query}\"\n" +
            $"Captured context: {shotInfo}; {elementInfo}.\n" +
            $"(Switch to a real provider to get an actual answer.)";

        return ProviderResult.Ok(text);
    }

    private static string Trim(string s, int max) =>
        s.Length <= max ? s : s[..max] + "\u2026";
}
