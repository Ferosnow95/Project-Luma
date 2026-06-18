namespace SlashCursor.Core;

/// <summary>
/// User-configurable settings. The API key is NOT stored here in plain text;
/// it is encrypted via DPAPI and kept in <see cref="OpenAiKeyProtected"/>.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Vision model id used by the OpenAI provider.</summary>
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>Square capture size (px) around the cursor when not full-screen.</summary>
    public int CaptureBoxSize { get; set; } = 800;

    /// <summary>Capture the whole virtual desktop instead of a region.</summary>
    public bool CaptureWholeScreen { get; set; }

    /// <summary>Base64 of the DPAPI-protected OpenAI API key (current user scope).</summary>
    public string? OpenAiKeyProtected { get; set; }

    /// <summary>Base64 of the DPAPI-protected Google Gemini API key (current user scope).</summary>
    public string? GeminiKeyProtected { get; set; }

    /// <summary>Gemini model id, e.g. "gemini-2.5-flash".</summary>
    public string GeminiModel { get; set; } = "gemini-2.5-flash";

    /// <summary>Ollama server base URL (local, no key needed).</summary>
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";

    /// <summary>Local Ollama vision model, e.g. "llava" or "llama3.2-vision".</summary>
    public string OllamaModel { get; set; } = "llava";

    /// <summary>Google search mode for the browser provider: "web" or "images".</summary>
    public string GoogleSearchMode { get; set; } = "web";

    /// <summary>Active provider id: "mock", "gemini", "ollama", "openai", or "google".</summary>
    public string ActiveProvider { get; set; } = "google";
}
