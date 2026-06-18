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
  // ---- Organization (deterministic, no AI required) ----
  | { action: "createSection"; name?: string }
  | { action: "wrapInFrame"; name?: string }
  | { action: "arrangeGrid"; columns?: number; gap?: number }
  | { action: "autoOrganize"; basis?: "name" | "proximity"; gap?: number }
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

// ---- Organization helpers (deterministic, no AI) ----

/** Nodes we can sensibly organize (have a position + size on the canvas). */
type Positioned = SceneNode & LayoutMixin;

function isPositioned(n: SceneNode): n is Positioned {
  return "x" in n && "y" in n && "width" in n && "height" in n;
}

/** Top-left-most origin of a set of nodes, used as the layout anchor. */
function originOf(nodes: Positioned[]): { x: number; y: number } {
  return {
    x: Math.min(...nodes.map((n) => n.x)),
    y: Math.min(...nodes.map((n) => n.y)),
  };
}

/**
 * Lay nodes out in a grid (row-major), preserving their reading order
 * (sorted by current y, then x). Rows size to the tallest item in each row.
 */
function layoutGrid(nodes: Positioned[], columns: number, gap: number): void {
  const sorted = [...nodes].sort((a, b) => (a.y - b.y) || (a.x - b.x));
  const { x: ox, y: oy } = originOf(sorted);
  const cols = Math.max(1, columns);

  let row = 0;
  let cursorX = ox;
  let rowTop = oy;
  let rowHeight = 0;
  sorted.forEach((node, i) => {
    const col = i % cols;
    if (col === 0 && i > 0) {
      rowTop += rowHeight + gap;
      cursorX = ox;
      rowHeight = 0;
      row++;
    }
    node.x = cursorX;
    node.y = rowTop;
    cursorX += node.width + gap;
    rowHeight = Math.max(rowHeight, node.height);
  });
  void row;
}

/** Group nodes by the first token of their name (split on / - _ : or space). */
function clusterByName(nodes: Positioned[]): Map<string, Positioned[]> {
  const groups = new Map<string, Positioned[]>();
  for (const n of nodes) {
    const token = (n.name.split(/[\/\-_:\s]/)[0] || "Untitled").trim() || "Untitled";
    const key = token.toLowerCase();
    (groups.get(key) ?? groups.set(key, []).get(key)!).push(n);
  }
  return groups;
}

/**
 * Group nodes by spatial proximity: connected components where two nodes are
 * "near" if the gap between their bounding boxes is below a threshold.
 */
function clusterByProximity(nodes: Positioned[], threshold: number): Positioned[][] {
  const near = (a: Positioned, b: Positioned): boolean => {
    const gapX = Math.max(0, Math.max(a.x - (b.x + b.width), b.x - (a.x + a.width)));
    const gapY = Math.max(0, Math.max(a.y - (b.y + b.height), b.y - (a.y + a.height)));
    return gapX <= threshold && gapY <= threshold;
  };
  const remaining = new Set(nodes);
  const clusters: Positioned[][] = [];
  for (const start of nodes) {
    if (!remaining.has(start)) continue;
    const cluster: Positioned[] = [];
    const queue = [start];
    remaining.delete(start);
    while (queue.length) {
      const cur = queue.pop()!;
      cluster.push(cur);
      for (const other of [...remaining]) {
        if (near(cur, other)) {
          remaining.delete(other);
          queue.push(other);
        }
      }
    }
    clusters.push(cluster);
  }
  return clusters;
}

/** Wrap a set of positioned nodes in a Section sized to their bounds. */
function sectionFromNodes(nodes: Positioned[], name: string, pad = 40): SectionNode {
  const section = figma.createSection();
  section.name = name;
  const parent = nodes[0].parent ?? figma.currentPage;
  parent.appendChild(section);

  const minX = Math.min(...nodes.map((n) => n.x));
  const minY = Math.min(...nodes.map((n) => n.y));
  const maxX = Math.max(...nodes.map((n) => n.x + n.width));
  const maxY = Math.max(...nodes.map((n) => n.y + n.height));

  section.x = minX - pad;
  section.y = minY - pad;
  section.resizeWithoutConstraints(maxX - minX + pad * 2, maxY - minY + pad * 2);
  for (const n of nodes) section.appendChild(n);
  return section;
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

    case "createSection": {
      const nodes = sel.filter(isPositioned);
      if (nodes.length === 0) return "Select some frames/screens to put in a section.";
      const section = sectionFromNodes(nodes, act.name?.trim() || "Section");
      figma.currentPage.selection = [section];
      figma.viewport.scrollAndZoomIntoView([section]);
      return `Created section "${section.name}" with ${nodes.length} item(s).`;
    }

    case "wrapInFrame": {
      const nodes = sel.filter(isPositioned);
      if (nodes.length === 0) return "Select some layers to wrap in a frame.";
      const minX = Math.min(...nodes.map((n) => n.x));
      const minY = Math.min(...nodes.map((n) => n.y));
      const maxX = Math.max(...nodes.map((n) => n.x + n.width));
      const maxY = Math.max(...nodes.map((n) => n.y + n.height));
      const frame = figma.createFrame();
      frame.name = act.name?.trim() || "Frame";
      frame.x = minX;
      frame.y = minY;
      frame.resize(maxX - minX, maxY - minY);
      const parent = nodes[0].parent ?? figma.currentPage;
      parent.appendChild(frame);
      // Reparent, keeping visual position (frame-relative coords).
      for (const n of nodes) {
        const absX = n.x;
        const absY = n.y;
        frame.appendChild(n);
        n.x = absX - minX;
        n.y = absY - minY;
      }
      figma.currentPage.selection = [frame];
      return `Wrapped ${nodes.length} layer(s) in frame "${frame.name}".`;
    }

    case "arrangeGrid": {
      const nodes = sel.filter(isPositioned);
      if (nodes.length < 2) return "Select at least 2 items to arrange.";
      const cols = act.columns ?? Math.ceil(Math.sqrt(nodes.length));
      const gap = act.gap ?? 80;
      layoutGrid(nodes, cols, gap);
      figma.viewport.scrollAndZoomIntoView(nodes);
      return `Arranged ${nodes.length} item(s) into a ${cols}-column grid (gap ${gap}).`;
    }

    case "autoOrganize": {
      const nodes = sel.filter(isPositioned);
      if (nodes.length < 2) return "Select at least 2 screens to organize.";
      const gap = act.gap ?? 80;
      const basis = act.basis ?? "name";

      let clusters: Positioned[][];
      if (basis === "proximity") {
        clusters = clusterByProximity(nodes, 400);
      } else {
        // Name-based; fall back to proximity if everything lands in one bucket.
        const byName = clusterByName(nodes);
        clusters = [...byName.values()];
        if (clusters.length <= 1) clusters = clusterByProximity(nodes, 400);
      }

      // Tidy each cluster into a grid, then box it in a named section.
      const sections: SectionNode[] = [];
      clusters.forEach((cluster) => {
        const cols = Math.ceil(Math.sqrt(cluster.length));
        layoutGrid(cluster, cols, gap);
        const label =
          basis === "name"
            ? titleCase(firstToken(cluster[0].name))
            : `Group ${sections.length + 1}`;
        sections.push(sectionFromNodes(cluster, label));
      });

      // Spread the sections apart so they don't overlap.
      layoutGrid(sections.filter(isPositioned) as Positioned[], Math.ceil(Math.sqrt(sections.length)), 160);
      figma.currentPage.selection = sections;
      figma.viewport.scrollAndZoomIntoView(sections);
      return `Organized ${nodes.length} screen(s) into ${sections.length} section(s) by ${basis}.`;
    }

    default:
      return "Unknown action.";
  }
}

function firstToken(name: string): string {
  return (name.split(/[\/\-_:\s]/)[0] || "Group").trim() || "Group";
}

function titleCase(s: string): string {
  return s.charAt(0).toUpperCase() + s.slice(1);
}

