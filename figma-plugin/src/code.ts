// Luma — main thread (code.js). Has access to figma.* but NO DOM/network.
// Network + AI provider calls happen in ui.html (the iframe). The two sides
// talk via postMessage. This mirrors SlashCursor's split:
//   CursorChatController (orchestration)  ->  code.ts
//   BubbleWindow + IResponseProvider (UI + network) -> ui.html

import { executeAction, getSelectionContext, LumaAction } from "./actions";

// Example commands shown as suggestions in Figma's quick-action parameter bar
// (Quick actions: Ctrl/Cmd + / → type "Luma" → these appear as you type).
const SUGGESTIONS = [
  "organize these screens",
  "group into a section",
  "arrange in a grid",
  "wrap these in a frame",
  "duplicate this 3 times",
  "make this blue",
  "rename to Button/Primary",
  "opacity 50%",
  "autolayout vertical 16",
  "delete",
];

// Live suggestions for the parameter input ("/"-style palette inside Figma's bar).
figma.parameters.on("input", ({ query, result }) => {
  const q = query.trim().toLowerCase();
  const matches = q ? SUGGESTIONS.filter((s) => s.toLowerCase().includes(q)) : SUGGESTIONS;
  // Always allow the freeform text too, so any phrasing reaches the AI/mock parser.
  result.setSuggestions(q && !matches.includes(query) ? [query, ...matches] : matches);
});

// Fired on every launch (menu, relaunch button, shortcut, or parameter entry).
figma.on("run", ({ parameters }: RunEvent) => {
  const prefill = parameters?.query?.trim() || null;
  launch(prefill);
});

function launch(prefill: string | null) {
  // Open as the small collapsed companion pill; the UI expands on click
  // (and resizes the window via a "resize" message).
  figma.showUI(__html__, { width: 240, height: 56, themeColors: true, title: "Luma" });

  // Push the current selection ("what the cursor points at") to the UI on open
  // and whenever it changes — so the AI always has fresh context.
  pushSelection();
  figma.on("selectionchange", pushSelection);

  // If the user typed a command in the quick-action bar, prefill + auto-run it.
  if (prefill) {
    figma.ui.postMessage({ type: "prefill", query: prefill, autorun: true });
  }
}


function pushSelection() {
  figma.ui.postMessage({ type: "selection", context: getSelectionContext() });
}


figma.ui.onmessage = async (msg: { type: string; action?: LumaAction; w?: number; h?: number }) => {
  if (msg.type === "run-action" && msg.action) {
    try {
      const result = await executeAction(msg.action);
      figma.ui.postMessage({ type: "result", ok: true, message: result });
      figma.notify(result);
    } catch (e) {
      const message = e instanceof Error ? e.message : String(e);
      figma.ui.postMessage({ type: "result", ok: false, message });
      figma.notify(`Luma error: ${message}`, { error: true });
    }
  } else if (msg.type === "resize" && msg.w && msg.h) {
    figma.ui.resize(msg.w, msg.h);
  } else if (msg.type === "close") {
    figma.closePlugin();
  }
};
