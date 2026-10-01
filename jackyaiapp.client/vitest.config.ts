import { fileURLToPath, URL } from 'node:url';

import { defineConfig } from 'vitest/config';

// Deliberately separate from vite.config.ts, not merged into it: vite.config.ts generates a local
// HTTPS dev cert via `dotnet dev-certs` as an import-time side effect (see its isDev block), which
// has no business running during a test import and would make `npm test` depend on the .NET SDK
// being installed. Only the pieces tests actually need — the `@` alias and jsdom — are duplicated
// here.
export default defineConfig({
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    fs: {
      // The repo root, so a test may `?raw`-import a backend file to check the client against it.
      // Tests only — the dev server, which serves this package to a browser, is configured in
      // vite.config.ts and keeps its default sandbox.
      allow: [fileURLToPath(new URL('..', import.meta.url))],
    },
  },
  test: {
    environment: 'jsdom',
  },
});
