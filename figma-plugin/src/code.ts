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
  // A compact, focused bubble near the toolbar.
  figma.showUI(__html__, { width: 380, height: 240, themeColors: true, title: "Luma" });

  // Push the current selection ("what the cursor points at") to the UI on open
  // and whenever it changes — so the AI always has fresh context.
  pushSelection();
  repositionNearSelection();
  figma.on("selectionchange", () => {
    pushSelection();
    repositionNearSelection();
  });

  // If the user typed a command in the quick-action bar, prefill + auto-run it.
  if (prefill) {
    figma.ui.postMessage({ type: "prefill", query: prefill, autorun: true });
  }
}

/**
 * "Companion" behaviour: a true cursor-follow is impossible (Figma never exposes
 * the live mouse position over the canvas). The closest native equivalent is to
 * make the bubble HUG the selected element and re-anchor on every selectionchange.
 *
 * We map the node's canvas-space bounds into screen pixels using the current
 * viewport, then nudge the UI to the node's top-right corner. Coordinates for
 * reposition() are relative to the visible canvas area; values are clamped so the
 * bubble stays on-screen. (Anchor offsets may want light calibration per setup.)
 */
function repositionNearSelection() {
  const sel = figma.currentPage.selection;
  if (sel.length === 0) return;

  const node = sel[0];
  const box = "absoluteBoundingBox" in node ? node.absoluteBoundingBox : null;
  if (!box) return;

  const { bounds, zoom } = figma.viewport;
  const viewW = bounds.width * zoom;
  const viewH = bounds.height * zoom;
  const UI_W = 380;
  const UI_H = 240;
  const GAP = 12;

  // Node's top-right corner, in screen px relative to the viewport's top-left.
  let x = (box.x + box.width - bounds.x) * zoom + GAP;
  let y = (box.y - bounds.y) * zoom;

  // Keep the bubble fully on-screen.
  x = Math.max(GAP, Math.min(x, viewW - UI_W - GAP));
  y = Math.max(GAP, Math.min(y, viewH - UI_H - GAP));

  figma.ui.reposition(x, y);
}


function pushSelection() {
  figma.ui.postMessage({ type: "selection", context: getSelectionContext() });
}


figma.ui.onmessage = async (msg: { type: string; action?: LumaAction }) => {
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
  } else if (msg.type === "close") {
    figma.closePlugin();
  }
};
