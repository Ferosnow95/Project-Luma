using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Glint.Core;

/// <summary>
/// Loads/saves <see cref="AppSettings"/> to %APPDATA%\Glint\settings.json
/// and handles encrypting/decrypting the OpenAI API key with Windows DPAPI
/// (current-user scope). The decrypted key only ever exists in memory.
/// </summary>
public static class SettingsStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Glint");

    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static AppSettings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load settings; using defaults", ex);
            Current = new AppSettings();
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save settings", ex);
        }
    }

    /// <summary>
    /// Returns the usable OpenAI key: the stored (decrypted) key if present,
    /// otherwise the OPENAI_API_KEY environment variable, otherwise null.
    /// </summary>
    public static string? GetOpenAiKey()
    {
        var stored = Current.OpenAiKeyProtected;
        if (!string.IsNullOrWhiteSpace(stored))
        {
            try
            {
                var enc = Convert.FromBase64String(stored);
                var data = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to decrypt stored OpenAI key", ex);
            }
        }

        var env = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        return string.IsNullOrWhiteSpace(env) ? null : env;
    }

    /// <summary>Encrypts and stores the key (or clears it when null/empty), then saves.</summary>
    public static void SetOpenAiKey(string? plainKey)
    {
        if (string.IsNullOrWhiteSpace(plainKey))
        {
            Current.OpenAiKeyProtected = null;
        }
        else
        {
            var data = Encoding.UTF8.GetBytes(plainKey.Trim());
            var enc = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            Current.OpenAiKeyProtected = Convert.ToBase64String(enc);
        }
        Save();
    }

    /// <summary>True if a key is available from storage or the environment.</summary>
    public static bool HasOpenAiKey() => !string.IsNullOrWhiteSpace(GetOpenAiKey());

    /// <summary>
    /// Returns the usable Gemini key: the stored (decrypted) key if present,
    /// otherwise the GEMINI_API_KEY (or GOOGLE_API_KEY) environment variable, otherwise null.
    /// </summary>
    public static string? GetGeminiKey()
    {
        var stored = Current.GeminiKeyProtected;
        if (!string.IsNullOrWhiteSpace(stored))
        {
            try
            {
                var enc = Convert.FromBase64String(stored);
                var data = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to decrypt stored Gemini key", ex);
            }
        }

        var env = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                  ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
        return string.IsNullOrWhiteSpace(env) ? null : env;
    }

    /// <summary>Encrypts and stores the Gemini key (or clears it when null/empty), then saves.</summary>
    public static void SetGeminiKey(string? plainKey)
    {
        if (string.IsNullOrWhiteSpace(plainKey))
        {
            Current.GeminiKeyProtected = null;
        }
        else
        {
            var data = Encoding.UTF8.GetBytes(plainKey.Trim());
            var enc = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            Current.GeminiKeyProtected = Convert.ToBase64String(enc);
        }
        Save();
    }

    /// <summary>True if a Gemini key is available from storage or the environment.</summary>
    public static bool HasGeminiKey() => !string.IsNullOrWhiteSpace(GetGeminiKey());
}
