import { fetchBaseQuery } from '@reduxjs/toolkit/query/react';

// 90s comfortably covers the slowest legitimate request (e.g. Finance/Portfolio AI analysis,
// which the backend itself bounds to 60s) while still aborting a genuinely hung fetch instead
// of leaving a loading spinner stuck forever. Individual slices can override if they need to.
const DEFAULT_TIMEOUT_MS = 90_000;

/**
 * Shared `fetchBaseQuery` factory for RTK Query API slices. Centralizes construction so every
 * slice gets the same request-timeout behavior instead of each hand-rolling its own
 * `fetchBaseQuery({...})` call, and gives a single place to add future cross-cutting request
 * config (headers, telemetry, etc.) without touching all 17 slices individually.
 *
 * `credentials` defaults to fetchBaseQuery's own default ('same-origin') — pass 'include'
 * only for slices that already relied on it (dashboard/payment flows).
 */
export function apiBaseQuery(baseUrl: string, credentials?: RequestCredentials) {
  return fetchBaseQuery({
    baseUrl,
    ...(credentials ? { credentials } : {}),
    timeout: DEFAULT_TIMEOUT_MS,
  });
}
