using System.Threading;
using System.Threading.Tasks;

namespace Glint.Providers;

/// <summary>
/// The context captured at the cursor when the user summons the bubble.
/// In Milestone 1 this carries a screenshot; later milestones add UIA text.
/// </summary>
public sealed class CursorContext
{
    /// <summary>PNG-encoded screenshot of the region around the cursor, if any.</summary>
    public byte[]? ScreenshotPng { get; init; }

    /// <summary>Plain text read from the element under the cursor (UIA), if any.</summary>
    public string? ElementText { get; init; }

    /// <summary>Screen coordinates of the cursor when summoned.</summary>
    public int CursorX { get; init; }
    public int CursorY { get; init; }
}

/// <summary>
/// A single response to show in the bubble.
/// </summary>
public sealed class ProviderResult
{
    public required string Text { get; init; }
    public bool IsError { get; init; }

    public static ProviderResult Ok(string text) => new() { Text = text };
    public static ProviderResult Error(string text) => new() { Text = text, IsError = true };
}

/// <summary>
/// A swappable response backend. Implementations: Mock, Google, OpenAI, Anthropic.
/// </summary>
public interface IResponseProvider
{
    /// <summary>Friendly name shown in the UI / tray menu.</summary>
    string Name { get; }

    /// <summary>
    /// Answer the user's query given the captured cursor context.
    /// </summary>
    Task<ProviderResult> AskAsync(string query, CursorContext context, CancellationToken ct);
}
