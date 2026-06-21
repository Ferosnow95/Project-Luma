# Glint

> Point. Press `/`. Ask.

**Glint** is a lightweight Windows overlay that puts an AI assistant wherever your
cursor is. Press the `/` key anywhere, and a Figma-style chat bubble springs up
at your pointer with a sparkle of light. Glint captures what you're pointing at
(a screenshot of the region around the cursor), lets you pick an action, and
answers your question in place — without ever leaving the app you're in.

---

## What it does

- **Summon anywhere** — A global hotkey (`/`) opens a chat bubble at the cursor,
  on top of any app. Press `Esc` or click away to dismiss.
- **Context capture** — Glint grabs a screenshot of the area around your cursor
  (configurable size, or the whole screen) and sends it as context to the AI.
- **Action pills** — Choose how you want to engage before you ask:
  - **Summarize** — condense what's on screen.
  - **Explain** — break it down in simple terms.
  - **Search** — look it up, with web-result cards (mocked for now) plus an
    AI summary.
  Scroll, use arrow keys, or click to pick a pill; `Enter` or click to select.
- **Bounding box** — After you pick an action, a dashed box shows exactly which
  region was captured.
- **Pluggable AI providers** — Switch between providers from the tray menu or
  Settings:
  | Provider | Cost | Notes |
  |---|---|---|
  | **Gemini** | Free tier | Vision model, uses `gemini-2.5-flash`. Needs a Google API key. |
  | **Ollama** | Free, local | Runs models like `llava` on your own machine. No key, fully offline. |
  | **OpenAI** | Paid | GPT-4o vision. Needs an API key with billing enabled. |
  | **Google (browser)** | Free | Opens a Google / Google Lens search in your browser. No key. |
  | **Mock** | Free | Canned responses for testing the UI. |
- **Secure key storage** — API keys are encrypted at rest with Windows DPAPI
  (per-user) and never written in plain text.
- **Runs in the tray** — No window clutter; Glint lives as a system-tray icon.

---

## Requirements

- **Windows 10 or 11** (x64).
- **.NET SDK 10** (`10.0.203` or newer) including the **Windows Desktop** runtime.
  - Check with: `dotnet --version`
  - Download: <https://dotnet.microsoft.com/download/dotnet/10.0>
- *(Optional)* **Ollama** if you want a free, fully-local model:
  <https://ollama.com> — then run `ollama pull llava`.

---

## Install & build

```powershell
# 1. Clone the repository
git clone https://github.com/Ferosnow95/Project-Luma.git
cd Project-Luma

# 2. Restore and build
dotnet build

# 3. Run
dotnet run --project src/Glint
```

Glint starts minimized to the system tray. Look for its icon near the clock.

### Build a standalone executable (optional)

```powershell
dotnet publish src/Glint -c Release -r win-x64 --self-contained false
```

The published app lands in
`src/Glint/bin/Release/net10.0-windows/win-x64/publish/`. Double-click
`Glint.exe` to run it without the SDK.

---

## First-time setup

1. **Launch** the app (`dotnet run --project src/Glint`).
2. **Open Settings** — double-click the tray icon, or right-click it and choose
   **Settings…**.
3. **Pick a provider** and add a key if it needs one:
   - **Gemini (recommended, free):** paste your Google API key. Get one at
     <https://aistudio.google.com/app/apikey>. Keep the model on
     `gemini-2.5-flash`.
   - **Ollama (free, offline):** install Ollama, run `ollama pull llava`, and
     select Ollama — no key required.
   - **OpenAI:** paste an API key with billing enabled.
   - **Google (browser):** nothing to configure; it opens searches in your
     browser.
4. **Save.** Your choice is remembered for next time.

---

## How to use

1. Point your cursor at something on screen.
2. Press **`/`**.
3. Pick an action pill — **Summarize**, **Explain**, or **Search**
   (scroll / arrow keys / click, then `Enter` or click).
4. Type your question and press **`Enter`**.
5. Read the answer in the bubble. In **Search** mode, click a result card to open
   it in your browser.
6. Press **`Esc`** or click outside the bubble to close it.

---

## Configuration & data locations

| What | Where |
|---|---|
| Settings (provider, model, capture size) | `%APPDATA%\Glint\settings.json` |
| Encrypted API keys | inside `settings.json` (DPAPI, per-user) |
| Log file | `%TEMP%\Glint.log` |
| Saved search screenshots | `%TEMP%\Glint\` |

You can also provide keys via environment variables instead of Settings:
`GEMINI_API_KEY` / `GOOGLE_API_KEY` for Gemini, `OPENAI_API_KEY` for OpenAI.

---

## Project layout

```
Glint.slnx                 Solution (.slnx)
src/Glint/
  App.xaml(.cs)                  Tray app + provider wiring (headless host)
  Core/                          Settings, controller, logging
  Input/                         Global keyboard hook, focus guard
  Capture/                       Screen capture around the cursor
  Overlay/BubbleWindow.xaml(.cs) The cursor chat bubble + animations
  Providers/                     Gemini, Ollama, OpenAI, Google, Mock
  Settings/                      Settings window
```

---

## Tech

- **C# / WPF on .NET 10** (`net10.0-windows`), with WinForms interop for the
  system-tray icon and screenshots.
- **Win32 interop** for the global `/` hotkey, foreground focus handling, and a
  transparent, click-blocking, multi-monitor overlay.
- **DPAPI** (`ProtectedData`) for at-rest key encryption.

---

## Troubleshooting

- **The bubble opens but I can't type.** Glint takes foreground focus from the
  active window; if focus didn't transfer, click the bubble once and type.
- **Gemini returns a 404 for a model.** Use `gemini-2.5-flash`; some accounts
  don't have access to other vision models.
- **OpenAI says "insufficient quota".** Enable billing on your OpenAI account.
- **Ollama errors.** Make sure Ollama is installed and running, and that you've
  pulled a vision model (`ollama pull llava`).
- Check `%TEMP%\Glint.log` for details.

---

## License

This project is provided as-is for personal use.
