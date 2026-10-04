import { beforeEach, describe, expect, it, vi } from 'vitest';

const sdk = vi.hoisted(() => ({
  track: vi.fn(),
  load: vi.fn(),
  ctor: vi.fn(),
  setAuth: vi.fn(),
}));

vi.mock('@microsoft/applicationinsights-web', () => ({
  ApplicationInsights: class {
    constructor(cfg: unknown) {
      sdk.ctor(cfg);
    }
    loadAppInsights = sdk.load;
    trackPageView = sdk.track;
    setAuthenticatedUserContext = sdk.setAuth;
  },
}));

async function freshModule() {
  vi.resetModules();
  return import('./analytics');
}

function mockConfig(connectionString: string | null) {
  vi.stubGlobal(
    'fetch',
    vi.fn().mockResolvedValue({ ok: true, json: async () => ({ connectionString }) }),
  );
}

describe('pageNameFor', () => {
  it('collapses dictionary word pages and dashboards, keeps others, drops query', async () => {
    const { pageNameFor } = await freshModule();
    expect(pageNameFor('/english/dictionary/apple')).toBe('/english/dictionary/:word');
    expect(pageNameFor('/english/dictionary/ice%20cream')).toBe('/english/dictionary/:word');
    expect(pageNameFor('/dashboards/abc')).toBe('/dashboards/:id');
    expect(pageNameFor('/english/dictionary')).toBe('/english/dictionary');
    expect(pageNameFor('/finance')).toBe('/finance');
  });
});

describe('trackPageView', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.stubEnv('DEV', false);
  });

  it('sends a page view with aggregated name, bare uri and isAuthenticated; never sets user context', async () => {
    mockConfig('InstrumentationKey=abc');
    const { trackPageView } = await freshModule();
    await trackPageView('/english/dictionary/apple', true);
    expect(sdk.track).toHaveBeenCalledWith({
      name: '/english/dictionary/:word',
      uri: '/english/dictionary/apple',
      properties: { isAuthenticated: true },
    });
    expect(sdk.ctor.mock.calls[0][0].config).toMatchObject({
      connectionString: 'InstrumentationKey=abc',
      disableAjaxTracking: true,
      disableFetchTracking: true,
      disableExceptionTracking: true,
    });
    expect(sdk.setAuth).not.toHaveBeenCalled();
  });

  it('initialises the SDK once across navigations', async () => {
    mockConfig('InstrumentationKey=abc');
    const { trackPageView } = await freshModule();
    await trackPageView('/finance', false);
    await trackPageView('/portfolio', false);
    expect(sdk.ctor).toHaveBeenCalledTimes(1);
    expect(sdk.track).toHaveBeenCalledTimes(2);
  });

  it('is a no-op when the connection string is null', async () => {
    mockConfig(null);
    const { trackPageView } = await freshModule();
    await trackPageView('/finance', false);
    expect(sdk.ctor).not.toHaveBeenCalled();
    expect(sdk.track).not.toHaveBeenCalled();
  });

  it('is a no-op and silent when the config request fails', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('offline')));
    const { trackPageView } = await freshModule();
    await expect(trackPageView('/finance', false)).resolves.toBeUndefined();
    expect(sdk.ctor).not.toHaveBeenCalled();
  });

  it('does nothing in DEV (no config request)', async () => {
    vi.stubEnv('DEV', true);
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
    const { trackPageView } = await freshModule();
    await trackPageView('/finance', false);
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
