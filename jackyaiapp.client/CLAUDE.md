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
