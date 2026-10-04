import { matchPath } from 'react-router-dom';

// Routes whose param makes the pathname unbounded (e.g. tens of thousands of dictionary words)
// are reported under their pattern so Application Insights doesn't get a distinct page name each.
const AGGREGATED_PATTERNS = ['/english/dictionary/:word', '/dashboards/:id'] as const;

/** Page name for telemetry: the route pattern for param routes, else the bare pathname (no query). */
export function pageNameFor(pathname: string): string {
  const pattern = AGGREGATED_PATTERNS.find((p) => matchPath({ path: p, end: true }, pathname));
  return pattern ?? pathname;
}

interface PageViewClient {
  trackPageView: (pv: {
    name: string;
    uri: string;
    properties: { isAuthenticated: boolean };
  }) => void;
}

let clientPromise: Promise<PageViewClient | null> | null = null;

// Loaded once: fetch the (non-secret) connection string, and only then pull the SDK in as its own
// chunk. Null (and a permanent no-op) in dev, when unset, or on any failure — analytics must never
// surface an error to the user.
function loadClient(): Promise<PageViewClient | null> {
  if (import.meta.env.DEV) return Promise.resolve(null);
  clientPromise ??= (async () => {
    try {
      const res = await fetch('/api/config/telemetry');
      if (!res.ok) return null;
      const { connectionString } = (await res.json()) as { connectionString: string | null };
      if (!connectionString) return null;
      const { ApplicationInsights } = await import('@microsoft/applicationinsights-web');
      const ai = new ApplicationInsights({
        config: {
          connectionString,
          disableAjaxTracking: true,
          disableFetchTracking: true,
          disableExceptionTracking: true,
          enableAutoRouteTracking: false,
        },
      });
      ai.loadAppInsights();
      // Intentionally never call setAuthenticatedUserContext: usage stats stay anonymous.
      return ai;
    } catch {
      return null;
    }
  })();
  return clientPromise;
}

export async function trackPageView(pathname: string, isAuthenticated: boolean): Promise<void> {
  const client = await loadClient();
  client?.trackPageView({
    name: pageNameFor(pathname),
    uri: pathname,
    properties: { isAuthenticated },
  });
}
