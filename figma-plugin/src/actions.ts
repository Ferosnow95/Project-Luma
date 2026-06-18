// Luma — presentation toolbox engine (runs on Figma's main thread, has figma.* access).
// Every export is a single-click "do the multi-step thing for me" deck action.
// No AI, no network — deterministic operations on frames and prototype reactions.

export type Order = "position" | "name";

export type TransitionKind =
  | "SMART_ANIMATE"
  | "DISSOLVE"
  | "MOVE_IN"
  | "SLIDE_IN"
  | "PUSH"
  | "INSTANT";

export type EasingChoice =
  | "EASE_IN_AND_OUT"
  | "EASE_IN"
  | "EASE_OUT"
  | "LINEAR"
  | "GENTLE"
  | "QUICK"
  | "BOUNCY"
  | "SLOW"
  | "CUSTOM";

export type TriggerKind = "ON_CLICK" | "ARROW_KEYS" | "AUTO_ADVANCE";

export interface ConnectOptions {
  order: Order;
  transition: TransitionKind;
  direction: "LEFT" | "RIGHT" | "TOP" | "BOTTOM";
  easing: EasingChoice;
  bezier: { x1: number; y1: number; x2: number; y2: number };
  duration: number; // milliseconds (from UI)
  trigger: TriggerKind;
  autoDelay: number; // seconds, used for AUTO_ADVANCE
  loop: boolean;
  back: boolean;
  flowName: string;
}

export interface PageNumberOptions {
  format: "plain" | "fraction" | "labeled";
  position: "bottom-right" | "bottom-center" | "bottom-left" | "top-right";
  skipFirst: boolean;
  order: Order;
}

export interface NormalizeOptions {
  width: number;
  height: number;
}

export interface TidyOptions {
  gap: number;
}

export interface DeckInfo {
  selectedFrames: number;
  totalFrames: number;
  names: string[];
}

type DeckFrame = FrameNode | ComponentNode | InstanceNode;

const ARROW_RIGHT = 39;
const ARROW_LEFT = 37;
const PAGE_NUMBER_MARK = "luma-page-number";

// ---------------------------------------------------------------------------
// Frame collection + ordering
// ---------------------------------------------------------------------------

function isDeckFrame(n: SceneNode): n is DeckFrame {
  return n.type === "FRAME" || n.type === "COMPONENT" || n.type === "INSTANCE";
}

function collectFrames(selection: readonly SceneNode[]): DeckFrame[] {
  const out: DeckFrame[] = [];
  if (selection.length) {
    for (const n of selection) {
      if (n.type === "SECTION") {
        for (const c of n.children) if (isDeckFrame(c)) out.push(c);
      } else if (isDeckFrame(n)) {
        out.push(n);
      }
    }
  } else {
    for (const c of figma.currentPage.children) if (isDeckFrame(c)) out.push(c);
  }
  return out;
}

function naturalCompare(a: string, b: string): number {
  return a.localeCompare(b, undefined, { numeric: true, sensitivity: "base" });
}

// Reading order: group into rows by Y, then left-to-right within a row.
function orderFrames(frames: DeckFrame[], order: Order): DeckFrame[] {
  const arr = frames.slice();
  if (order === "name") {
    arr.sort((a, b) => naturalCompare(a.name, b.name));
    return arr;
  }
  arr.sort((a, b) => {
    const rowGap = Math.min(a.height, b.height) * 0.5;
    if (Math.abs(a.y - b.y) > rowGap) return a.y - b.y;
    return a.x - b.x;
  });
  return arr;
}

// ---------------------------------------------------------------------------
// Slide Flow — wire prototype reactions (the flagship)
// ---------------------------------------------------------------------------

function buildEasing(opts: ConnectOptions): Easing {
  if (opts.easing === "CUSTOM") {
    return { type: "CUSTOM_CUBIC_BEZIER", easingFunctionCubicBezier: opts.bezier };
  }
  return { type: opts.easing } as Easing;
}

function buildTransition(opts: ConnectOptions): Transition | null {
  if (opts.transition === "INSTANT") return null;
  const easing = buildEasing(opts);
  const duration = Math.max(0, opts.duration) / 1000;
  if (opts.transition === "SMART_ANIMATE" || opts.transition === "DISSOLVE") {
    return { type: opts.transition, easing, duration };
  }
  return {
    type: opts.transition, // MOVE_IN | SLIDE_IN | PUSH
    direction: opts.direction,
    matchLayers: false,
    easing,
    duration,
  };
}

function navAction(destinationId: string, transition: Transition | null): Action {
  return {
    type: "NODE",
    destinationId,
    navigation: "NAVIGATE",
    transition,
    preserveScrollPosition: false,
    resetVideoPosition: false,
  };
}

function forwardTrigger(opts: ConnectOptions): Trigger {
  if (opts.trigger === "ARROW_KEYS") {
    return { type: "ON_KEY_DOWN", device: "KEYBOARD", keyCodes: [ARROW_RIGHT] };
  }
  if (opts.trigger === "AUTO_ADVANCE") {
    return { type: "AFTER_TIMEOUT", timeout: Math.max(0.1, opts.autoDelay) };
  }
  return { type: "ON_CLICK" };
}

export async function connectSlides(opts: ConnectOptions): Promise<string> {
  const frames = orderFrames(collectFrames(figma.currentPage.selection), opts.order);
  if (frames.length < 2) {
    throw new Error("Select at least 2 frames (or open a page with 2+ frames) to connect.");
  }

  const transition = buildTransition(opts);

  for (let i = 0; i < frames.length; i++) {
    const node = frames[i];
    const reactions: Reaction[] = [];
    const next = frames[i + 1];
    const prev = frames[i - 1];

    if (next) {
      reactions.push({ trigger: forwardTrigger(opts), actions: [navAction(next.id, transition)] });
    } else if (opts.loop) {
      reactions.push({ trigger: forwardTrigger(opts), actions: [navAction(frames[0].id, transition)] });
    }

    if (opts.back && opts.trigger === "ARROW_KEYS" && prev) {
      reactions.push({
        trigger: { type: "ON_KEY_DOWN", device: "KEYBOARD", keyCodes: [ARROW_LEFT] },
        actions: [navAction(prev.id, transition)],
      });
    }

    await node.setReactionsAsync(reactions);
  }

  figma.currentPage.flowStartingPoints = [
    { nodeId: frames[0].id, name: opts.flowName || "Slide Flow" },
  ];

  const triggerLabel =
    opts.trigger === "ARROW_KEYS" ? "arrow keys" : opts.trigger === "AUTO_ADVANCE" ? "auto-advance" : "click";
  return `Connected ${frames.length} slides · ${prettyTransition(opts.transition)} · ${triggerLabel}.`;
}

function prettyTransition(t: TransitionKind): string {
  switch (t) {
    case "SMART_ANIMATE": return "Smart Animate";
    case "DISSOLVE": return "Dissolve";
    case "MOVE_IN": return "Move in";
    case "SLIDE_IN": return "Slide in";
    case "PUSH": return "Push";
    case "INSTANT": return "Instant";
  }
}

// ---------------------------------------------------------------------------
// Page numbers
// ---------------------------------------------------------------------------

function formatNumber(format: PageNumberOptions["format"], n: number, total: number): string {
  if (format === "fraction") return `${n} / ${total}`;
  if (format === "labeled") return `Slide ${n}`;
  return `${n}`;
}

function positionLabel(text: TextNode, frame: DeckFrame, position: PageNumberOptions["position"]): void {
  const margin = 40;
  const w = frame.width;
  const h = frame.height;
  const tw = text.width;
  const th = text.height;
  switch (position) {
    case "bottom-right": text.x = w - tw - margin; text.y = h - th - margin; break;
    case "bottom-center": text.x = (w - tw) / 2; text.y = h - th - margin; break;
    case "bottom-left": text.x = margin; text.y = h - th - margin; break;
    case "top-right": text.x = w - tw - margin; text.y = margin; break;
  }
}

function findPageNumber(frame: DeckFrame): TextNode | null {
  const found = frame.findOne((n) => n.type === "TEXT" && n.getPluginData(PAGE_NUMBER_MARK) === "1");
  return (found as TextNode) || null;
}

export async function addPageNumbers(opts: PageNumberOptions): Promise<string> {
  const frames = orderFrames(collectFrames(figma.currentPage.selection), opts.order);
  if (!frames.length) throw new Error("No frames found to number.");

  await figma.loadFontAsync({ family: "Inter", style: "Regular" });

  const total = opts.skipFirst ? frames.length - 1 : frames.length;
  let count = 0;

  for (let i = 0; i < frames.length; i++) {
    const frame = frames[i];
    if (opts.skipFirst && i === 0) {
      const existing = findPageNumber(frame);
      if (existing) existing.remove();
      continue;
    }
    count++;
    let text = findPageNumber(frame);
    if (!text) {
      text = figma.createText();
      text.setPluginData(PAGE_NUMBER_MARK, "1");
      frame.appendChild(text);
    }
    text.fontName = { family: "Inter", style: "Regular" };
    text.fontSize = 24;
    text.characters = formatNumber(opts.format, count, total);
    text.fills = [{ type: "SOLID", color: { r: 0.55, g: 0.55, b: 0.6 } }];
    positionLabel(text, frame, opts.position);
  }

  return `Numbered ${count} slides.`;
}

// ---------------------------------------------------------------------------
// Layout helpers
// ---------------------------------------------------------------------------

export async function normalizeDeck(opts: NormalizeOptions): Promise<string> {
  const frames = collectFrames(figma.currentPage.selection);
  if (!frames.length) throw new Error("Select frames to normalize.");
  for (const f of frames) f.resizeWithoutConstraints(opts.width, opts.height);
  return `Resized ${frames.length} frames to ${opts.width}×${opts.height}.`;
}

export async function tidyDeck(opts: TidyOptions): Promise<string> {
  const frames = orderFrames(collectFrames(figma.currentPage.selection), "position");
  if (frames.length < 2) throw new Error("Select 2+ frames to tidy.");
  const y = Math.min(...frames.map((f) => f.y));
  let x = frames[0].x;
  for (const f of frames) {
    f.x = x;
    f.y = y;
    x += f.width + opts.gap;
  }
  return `Tidied ${frames.length} slides into a row.`;
}

// ---------------------------------------------------------------------------
// Selection info for the UI
// ---------------------------------------------------------------------------

export function getDeckInfo(): DeckInfo {
  const selectedFrames = collectFrames(figma.currentPage.selection);
  const totalFrames = figma.currentPage.children.filter(isDeckFrame).length;
  const ordered = orderFrames(selectedFrames.length ? selectedFrames : collectFrames([]), "position");
  return {
    selectedFrames: selectedFrames.length,
    totalFrames,
    names: ordered.slice(0, 40).map((f) => f.name),
  };
}
