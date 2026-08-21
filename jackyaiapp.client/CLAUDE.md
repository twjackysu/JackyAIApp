# CLAUDE.md (jackyaiapp.client)

Guidance specific to the frontend package. See the [root CLAUDE.md](../CLAUDE.md) for architecture, features, and cross-cutting conventions.

## Development Commands

```bash
npm run dev          # Start development server (https://localhost:5173)
npm run build        # TypeScript compilation + Vite production build
npm run lint         # ESLint with TypeScript, max 0 warnings
npm run preview      # Preview production build
```

## Frontend Structure

```
jackyaiapp.client/src/
├── apis/           # RTK Query API slices (dictionaryApis, financeApis, etc.)
├── [Feature]/      # Feature-based components (Dictionary/, Finance/, etc.)
├── components/     # Reusable UI components
├── redux/          # Store configuration
├── auth/           # Authentication components
└── constants/      # App configuration
```

## UI Conventions

### No hover-only tooltips

MUI's `<Tooltip>` is not used directly in feature code — on touch screens it only opens
after a long press, so anything living inside one is effectively invisible on mobile.
Use `@/components/hints` instead:

| Component | Use for | Behaviour |
| --- | --- | --- |
| `ActionTooltip` | Naming an icon-only control ("Refresh", "Close") | Hover tooltip on pointer devices; on touch it renders the child as-is and contributes an `aria-label` |
| `InfoPopover` | Explanation next to a label or an already-clickable control (formulas, grading scales) | Renders a "?" button that opens a click/tap/keyboard-triggered `Popover` |
| `ClickPopover` | Explanation attached to a non-interactive element (stat card, chip, heatmap cell) | Makes the element itself tappable to reveal the copy |

A **disabled** control gets neither hover nor tap, so the reason it is disabled belongs in
visible caption text next to it — not in any of the above.
