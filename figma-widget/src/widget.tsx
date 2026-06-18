// Luma — Figma WIDGET version. Lives ON the canvas (pans/zooms with your design)
// and expands in place into the companion toolbox. Reuses the exact same action
// engine as the plugin (../figma-plugin/src/actions.ts): clicking a tool reads the
// current selection and runs the matching LumaAction.
//
// Deterministic tools (organize/grid/duplicate/section/autolayout/…) need no
// network, so they run directly inside the widget's onClick handlers. AI freeform
// and future background-removal/image-editing will open an iframe later.

import { executeAction, LumaAction } from "../../figma-plugin/src/actions";

const { widget } = figma;
const { AutoLayout, Text, useSyncedState, Frame } = widget;

interface Tool {
  cat: string;
  label: string;
  desc: string;
  action?: LumaAction;
  soon?: boolean;
}

const CAT_COLOR: Record<string, string> = {
  Organize: "#0D99FF",
  Layout: "#34C759",
  Transform: "#AF52DE",
  Color: "#FF9500",
  Text: "#FF3B30",
  "Coming soon": "#8E8E93",
};

// First-pass widget tools. Parameterized actions use sensible defaults for now;
// inline inputs / AI come via the iframe drawer in a later pass.
const TOOLS: Tool[] = [
  { cat: "Organize", label: "Organize by name", desc: "Cluster screens into sections by name", action: { action: "autoOrganize", basis: "name" } },
  { cat: "Organize", label: "Organize by proximity", desc: "Group screens placed near each other", action: { action: "autoOrganize", basis: "proximity" } },
  { cat: "Organize", label: "Arrange in grid", desc: "Tidy the selection into a grid", action: { action: "arrangeGrid" } },
  { cat: "Organize", label: "Group into section", desc: "Box selected frames in a section", action: { action: "createSection" } },
  { cat: "Organize", label: "Wrap in frame", desc: "Wrap the selection in a frame", action: { action: "wrapInFrame" } },
  { cat: "Layout", label: "Auto-layout vertical", desc: "Stack with 16 spacing", action: { action: "autolayout", direction: "VERTICAL", spacing: 16 } },
  { cat: "Layout", label: "Auto-layout horizontal", desc: "Row with 16 spacing", action: { action: "autolayout", direction: "HORIZONTAL", spacing: 16 } },
  { cat: "Transform", label: "Duplicate ×2", desc: "Clone the selection twice", action: { action: "duplicate", count: 2 } },
  { cat: "Transform", label: "Delete", desc: "Remove the selection", action: { action: "delete" } },
  { cat: "Color", label: "Fill blue", desc: "Set a solid #0D99FF fill", action: { action: "setFill", color: "#0D99FF" } },
  { cat: "Color", label: "Opacity 50%", desc: "Set transparency to 50%", action: { action: "opacity", value: 0.5 } },
  { cat: "Coming soon", label: "Remove background", desc: "Knock out image backgrounds", soon: true },
  { cat: "Coming soon", label: "Image editing", desc: "Crop, adjust, filters", soon: true },
  { cat: "Coming soon", label: "Connect screens", desc: "Wire prototype flows on click", soon: true },
];

const SHADOW = {
  type: "drop-shadow" as const,
  color: { r: 0, g: 0, b: 0, a: 0.14 },
  offset: { x: 0, y: 4 },
  blur: 16,
};

async function runAction(action: LumaAction) {
  try {
    const result = await executeAction(action);
    figma.notify(result);
  } catch (e) {
    const message = e instanceof Error ? e.message : String(e);
    figma.notify(`Luma error: ${message}`, { error: true });
  }
}

function CatSwatch({ color }: { color: string }) {
  return <Frame width={20} height={20} cornerRadius={5} fill={`${color}22`} stroke={`${color}55`} />;
}

function ToolRow({ tool }: { tool: Tool }) {
  return (
    <AutoLayout
      direction="horizontal"
      width="fill-parent"
      verticalAlignItems="center"
      spacing={10}
      padding={{ vertical: 7, horizontal: 8 }}
      cornerRadius={8}
      opacity={tool.soon ? 0.5 : 1}
      hoverStyle={tool.soon ? undefined : { fill: "#F2F3F5" }}
      onClick={tool.soon || !tool.action ? undefined : () => runAction(tool.action!)}
    >
      <CatSwatch color={CAT_COLOR[tool.cat] || "#999999"} />
      <AutoLayout direction="vertical" width="fill-parent" spacing={1}>
        <Text fontSize={13} fontWeight={500} fill="#1A1A1A">
          {tool.label}
        </Text>
        <Text fontSize={11} fill="#1A1A1A" opacity={0.55}>
          {tool.desc}
        </Text>
      </AutoLayout>
      {tool.soon ? (
        <AutoLayout cornerRadius={10} stroke="#E0E0E0" padding={{ vertical: 1, horizontal: 6 }}>
          <Text fontSize={9} fill="#1A1A1A" opacity={0.7}>
            SOON
          </Text>
        </AutoLayout>
      ) : null}
    </AutoLayout>
  );
}

function Luma() {
  const [open, setOpen] = useSyncedState("open", false);

  if (!open) {
    // Collapsed companion pill.
    return (
      <AutoLayout
        direction="horizontal"
        verticalAlignItems="center"
        spacing={8}
        padding={{ vertical: 10, horizontal: 16 }}
        cornerRadius={22}
        fill="#FFFFFF"
        stroke="#E6E6E6"
        effect={SHADOW}
        onClick={() => setOpen(true)}
      >
        <Frame width={9} height={9} cornerRadius={5} fill="#0D99FF" />
        <Text fontSize={14} fontWeight={600} fill="#1A1A1A">
          Luma
        </Text>
      </AutoLayout>
    );
  }

  // Expanded toolbox, grouped by category (headers inserted when the cat changes).
  const rows: FigmaDeclarativeNode[] = [];
  let lastCat: string | null = null;
  TOOLS.forEach((tool, i) => {
    if (tool.cat !== lastCat) {
      lastCat = tool.cat;
      rows.push(
        <Text key={`cat-${i}`} fontSize={10} fill="#1A1A1A" opacity={0.5} fontWeight={600}>
          {tool.cat.toUpperCase()}
        </Text>
      );
    }
    rows.push(<ToolRow key={i} tool={tool} />);
  });

  return (
    <AutoLayout
      direction="vertical"
      spacing={4}
      padding={10}
      cornerRadius={14}
      fill="#FFFFFF"
      stroke="#E6E6E6"
      width={300}
      effect={SHADOW}
    >
      <AutoLayout direction="horizontal" width="fill-parent" verticalAlignItems="center" padding={{ horizontal: 4, vertical: 2 }}>
        <Frame width={9} height={9} cornerRadius={5} fill="#0D99FF" />
        <AutoLayout width={8} height={1} />
        <Text fontSize={14} fontWeight={700} fill="#1A1A1A">
          Luma
        </Text>
        <AutoLayout width="fill-parent" height={1} />
        <Text fontSize={16} fill="#1A1A1A" opacity={0.5} onClick={() => setOpen(false)}>
          ×
        </Text>
      </AutoLayout>
      {rows}
    </AutoLayout>
  );
}

widget.register(Luma);
