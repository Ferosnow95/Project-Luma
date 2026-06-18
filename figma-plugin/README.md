# Luma — Figma plugin

The Figma incarnation of **SlashCursor**: summon an AI at your cursor and do quick
design tasks by talking to it. Instead of pointing at pixels on screen, you point
at **selected nodes**; instead of an answer in a bubble, the AI returns a
**structured action** that the plugin executes on the canvas.

## Architecture (mirrors the WPF app)

| SlashCursor (WPF)            | Luma (Figma)                                  |
| ---------------------------- | --------------------------------------------- |
| `CursorChatController`       | `src/code.ts` (main thread, owns `figma.*`)   |
| `CursorContext` (screenshot) | `SelectionContext` (selected nodes)           |
| `BubbleWindow` overlay       | `ui.html` (iframe command bubble)             |
| `IResponseProvider`          | `mockProvider` / `geminiProvider` in `ui.html`|
| Answer text                  | `LumaAction` → `executeAction()`              |

The plugin runs in two sandboxes that talk via `postMessage`:

- **`code.ts`** — read selection, mutate nodes. No DOM/network.
- **`ui.html`** — the bubble UI + AI/network calls.

## Develop

```powershell
cd figma-plugin
npm install
npm run watch   # esbuild bundles src/*.ts -> dist/code.js on save
npm run typecheck   # optional: tsc type-check (no emit)
```

> The main thread (`code.ts`) is **bundled** into a single `dist/code.js` IIFE
> with esbuild. Figma's plugin sandbox runs one non-module script and rejects
> `import`/`export`, so we must bundle rather than emit raw ES modules.

Then in Figma desktop: **Plugins → Development → Import plugin from manifest…**
and pick `figma-plugin/manifest.json`.

## Try it (Mock provider, no API key)

Select a layer, open Luma, and type:

- `duplicate this 3 times`
- `make this blue` / `fill #2D7FF9`
- `rename to Button/Primary`
- `opacity 50%`
- `autolayout vertical 16`
- `move right 100`
- `delete`

Switch the provider dropdown to **Gemini** and paste a key to use the real model
(returns the same structured `LumaAction` JSON).

## Extend the toolbox

Add a case to `LumaAction` and the `switch` in `src/actions.ts`, then teach the
providers about it (a keyword rule in `mockProvider`, and the action list in the
Gemini system prompt).
