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
├── apis/           # RTK Query: the shared api.ts and the feature slices (dictionaryApis, financeApis, etc.)
├── [Feature]/      # Feature-based components (Dictionary/, Finance/, etc.)
├── components/     # Reusable UI components
├── redux/          # Store configuration
├── auth/           # Authentication components
└── constants/      # App configuration
```

## API slices

- A feature in the shared API (`apis/api.ts`: repository, review, stats, daily challenge, exam)
  adds endpoints with `api.injectEndpoints` in its own `apis/[feature]/index.ts`, paths relative to
  `/api`. A new tag goes in `TAG_TYPES` there.
- When a mutation changes data another feature shows, it invalidates that data's tag with
  `invalidatesTags: onSuccess(...)` (not a bare array: RTK Query also applies that to a mutation the
  server answered with an error). `apis/api.test.ts` pins which mutation refreshes what.

## Component structure

- A component that outgrows one file becomes a folder of the same name: `index.tsx` keeps the
  composition (and the export, so imports do not change), each section is its own file, and a rule the
  JSX was deciding inline — which notice wins, what is shown when — is a pure function with its own
  tests. `Portfolio/DrawdownBrakeCard/` is the pattern: why advice is held back is `noAdviceReason`
  (one reason, in order), the prices it is computed at `useStrategyPrices`
- A feature's page is already its folder's `index.tsx`, so its sections become files beside it and the
  page keeps only its states (loading, error, empty, the dashboard) — `Portfolio/index.tsx` with
  `PortfolioHeader`, `PortfolioDashboard`, `NetWorthCard`, `AllocationCard`. A section takes the DTO it
  shows (`summary`), not one prop per field
- A component whose bulk is state rather than markup splits into hooks instead: the component keeps
  the layout and wires them together. `components/aiChat` (the AI chat shared by the apps; Finance's assistant is its first user) is the pattern: the conversation
  and its queue are `useChatConversation`, the picked files `usePendingAttachments`, the preview
  `usePreview`
- Split without changing what renders: the component's existing tests stay as they are and must pass.
  One without tests gets them first, run against the code as it is (`Portfolio/index.test.tsx`)

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
