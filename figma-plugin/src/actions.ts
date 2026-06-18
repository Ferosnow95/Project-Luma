// Luma — action toolbox (runs in Figma's main thread, has access to figma.*)
//
// This mirrors the "do something to what the cursor points at" idea from the
// WPF SlashCursor app. Instead of showing an answer in a bubble, the AI returns
// a structured LumaAction and we EXECUTE it against the current selection.
//
// The structured-action pattern (vs. running raw AI-generated code) keeps the
// plugin safe and predictable. Add new capabilities by extending LumaAction
// and the switch in executeAction().

export type LumaAction =
  | { action: "duplicate"; count?: number }
  | { action: "rename"; name: string }
  | { action: "setFill"; color: string } // hex, e.g. "#2D7FF9"
  | { action: "move"; dx: number; dy: number }
  | { action: "resize"; width?: number; height?: number }
  | { action: "opacity"; value: number } // 0..1
  | { action: "autolayout"; direction?: "HORIZONTAL" | "VERTICAL"; spacing?: number }
  | { action: "delete" }
  | { action: "noop"; message: string }; // info / "didn't understand"

export interface SelectionNodeInfo {
  id: string;
  name: string;
  type: string;
  width?: number;
  height?: number;
  x?: number;
  y?: number;
}

/** Snapshot of what the user is "pointing at" — the Figma equivalent of CursorContext. */
export interface SelectionContext {
  count: number;
  nodes: SelectionNodeInfo[];
}

export function getSelectionContext(): SelectionContext {
  const sel = figma.currentPage.selection;
  return {
    count: sel.length,
    nodes: sel.map((n) => {
      const info: SelectionNodeInfo = { id: n.id, name: n.name, type: n.type };
      if ("width" in n) info.width = Math.round((n as LayoutMixin).width);
      if ("height" in n) info.height = Math.round((n as LayoutMixin).height);
      if ("x" in n) info.x = Math.round((n as LayoutMixin).x);
      if ("y" in n) info.y = Math.round((n as LayoutMixin).y);
      return info;
    }),
  };
}

function hexToRgb(hex: string): RGB {
  const clean = hex.replace("#", "").trim();
  const full =
    clean.length === 3
      ? clean.split("").map((c) => c + c).join("")
      : clean.padEnd(6, "0").slice(0, 6);
  const num = parseInt(full, 16);
  return {
    r: ((num >> 16) & 255) / 255,
    g: ((num >> 8) & 255) / 255,
    b: (num & 255) / 255,
  };
}

/**
 * Execute a structured action against the current selection.
 * Returns a short human-readable result string for the bubble.
 */
export async function executeAction(act: LumaAction): Promise<string> {
  const sel = figma.currentPage.selection;

  if (act.action === "noop") return act.message;

  if (sel.length === 0) {
    return "Nothing selected — point Luma at an element first.";
  }

  switch (act.action) {
    case "duplicate": {
      const count = Math.max(1, Math.min(act.count ?? 1, 50));
      const created: SceneNode[] = [];
      for (const node of sel) {
        let prev = node;
        for (let i = 0; i < count; i++) {
          const clone = node.clone();
          if ("x" in clone && "x" in prev) {
            (clone as LayoutMixin).x = (prev as LayoutMixin).x + ((prev as LayoutMixin).width ?? 0) + 24;
            (clone as LayoutMixin).y = (prev as LayoutMixin).y;
          }
          node.parent?.appendChild(clone);
          created.push(clone);
          prev = clone;
        }
      }
      figma.currentPage.selection = created;
      return `Duplicated ${sel.length} item(s) ×${count}.`;
    }

    case "rename": {
      for (const node of sel) node.name = act.name;
      return `Renamed ${sel.length} layer(s) to "${act.name}".`;
    }

    case "setFill": {
      const rgb = hexToRgb(act.color);
      let changed = 0;
      for (const node of sel) {
        if ("fills" in node) {
          (node as GeometryMixin).fills = [{ type: "SOLID", color: rgb }];
          changed++;
        }
      }
      return `Set fill ${act.color} on ${changed} node(s).`;
    }

    case "move": {
      for (const node of sel) {
        if ("x" in node) {
          (node as LayoutMixin).x += act.dx;
          (node as LayoutMixin).y += act.dy;
        }
      }
      return `Moved ${sel.length} node(s) by (${act.dx}, ${act.dy}).`;
    }

    case "resize": {
      for (const node of sel) {
        if ("resize" in node) {
          const w = act.width ?? (node as LayoutMixin).width;
          const h = act.height ?? (node as LayoutMixin).height;
          (node as LayoutMixin & { resize(w: number, h: number): void }).resize(w, h);
        }
      }
      return `Resized ${sel.length} node(s).`;
    }

    case "opacity": {
      const v = Math.max(0, Math.min(act.value, 1));
      let changed = 0;
      for (const node of sel) {
        if ("opacity" in node) {
          (node as BlendMixin).opacity = v;
          changed++;
        }
      }
      return `Set opacity ${Math.round(v * 100)}% on ${changed} node(s).`;
    }

    case "autolayout": {
      let changed = 0;
      for (const node of sel) {
        if (node.type === "FRAME") {
          const frame = node as FrameNode;
          frame.layoutMode = act.direction ?? "VERTICAL";
          if (act.spacing != null) frame.itemSpacing = act.spacing;
          changed++;
        }
      }
      return changed
        ? `Applied auto-layout to ${changed} frame(s).`
        : "Auto-layout only applies to frames — none selected.";
    }

    case "delete": {
      const n = sel.length;
      for (const node of sel) node.remove();
      return `Deleted ${n} node(s).`;
    }

    default:
      return "Unknown action.";
  }
}
