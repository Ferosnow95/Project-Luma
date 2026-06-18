# Luma — Figma widget (on-canvas companion)

The widget version of Luma. Unlike the plugin (a floating window), this lives **on
the canvas** as a real node: it pans/zooms with your design and expands in place
into the companion toolbox. Clicking a tool reads your current selection and runs
the matching action.

It **reuses the plugin's action engine** directly:
`import { executeAction } from "../../figma-plugin/src/actions"`.

## Develop

```powershell
cd figma-widget
npm install
npm run watch       # esbuild bundles src/widget.tsx -> dist/code.js
npm run typecheck   # optional: tsc (no emit)
```

Then in Figma desktop: **Plugins → Development → Import plugin from manifest…** and
pick `figma-widget/manifest.json`. Insert the widget from the Resources/Widgets
menu, then click the Luma pill to expand the toolbox.

## What's in this first pass
- Collapsed **pill** on the canvas → click to expand.
- Vertical, grouped **tool list** (Organize / Layout / Transform / Color) built with
  widget primitives (AutoLayout, Text, Frame).
- Deterministic tools run directly in `onClick` (no network). Parameterized actions
  use sensible defaults for now (e.g. Duplicate ×2, Fill blue, Opacity 50%).
- "Coming soon" rows: Remove background, Image editing, Connect screens.

## Next
- Iframe "power drawer" (`figma.showUI`) for AI freeform + inline inputs + the
  network features (background removal, image editing).
- Prototype wiring for "Connect screens".
