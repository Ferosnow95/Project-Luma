// Luma — main thread (code.js). Has access to figma.* but NO DOM/network.
// Network + AI provider calls happen in ui.html (the iframe). The two sides
// talk via postMessage. This mirrors SlashCursor's split:
//   CursorChatController (orchestration)  ->  code.ts
//   BubbleWindow + IResponseProvider (UI + network) -> ui.html

import { executeAction, getSelectionContext, LumaAction } from "./actions";

// A compact, focused bubble near the toolbar.
figma.showUI(__html__, { width: 380, height: 240, themeColors: true, title: "Luma" });

// Push the current selection ("what the cursor points at") to the UI on open
// and whenever it changes — so the AI always has fresh context.
function pushSelection() {
  figma.ui.postMessage({ type: "selection", context: getSelectionContext() });
}
pushSelection();
figma.on("selectionchange", pushSelection);

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
