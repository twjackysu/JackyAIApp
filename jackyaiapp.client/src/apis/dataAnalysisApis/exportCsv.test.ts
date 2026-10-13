import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from 'vitest';

import { NETWORK_ERROR_MESSAGE } from '../utils/errorMessage';

import { exportDetailsCsv, filenameFromDisposition } from './exportCsv';

function csvResponse(headers: Record<string, string>) {
  return new Response('Region\r\nEMEA\r\n', {
    status: 200,
    headers: { 'Content-Type': 'text/csv; charset=utf-8', ...headers },
  });
}

describe('exportDetailsCsv', () => {
  let click: Mock<(this: HTMLAnchorElement) => void>;
  let downloadedAs: string | undefined;

  beforeEach(() => {
    downloadedAs = undefined;
    click = vi.fn(function (this: HTMLAnchorElement) {
      downloadedAs = this.download;
    });
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(click);
    URL.createObjectURL = vi.fn(() => 'blob:csv');
    URL.revokeObjectURL = vi.fn();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('posts the query without paging and downloads the file under the server name', async () => {
    const fetchMock = vi.fn(() =>
      Promise.resolve(
        csvResponse({
          'Content-Disposition': "attachment; filename*=UTF-8''dbo.Orders_20261013-120000.csv",
          'X-Exported-Count': '2',
          'X-Total-Count': '2',
        }),
      ),
    );
    vi.stubGlobal('fetch', fetchMock);

    const result = await exportDetailsCsv({
      context: { sourceId: 'src-1', tableName: 'dbo.Sales' },
      orderBy: [{ fieldName: 'OrderDate', descending: true }],
      filters: [{ fieldName: 'Region', values: ['EMEA'] }],
    });

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('/api/dataanalysis/details/export');
    expect(JSON.parse(init.body as string)).toEqual({
      context: { sourceId: 'src-1', tableName: 'dbo.Sales' },
      orderBy: [{ fieldName: 'OrderDate', descending: true }],
      filters: [{ fieldName: 'Region', values: ['EMEA'] }],
    });
    expect(click).toHaveBeenCalledOnce();
    expect(downloadedAs).toBe('dbo.Orders_20261013-120000.csv');
    expect(result).toEqual({ exported: 2, total: 2, filename: 'dbo.Orders_20261013-120000.csv' });
  });

  it('uses the session endpoint for uploaded data', async () => {
    const fetchMock = vi.fn(() => Promise.resolve(csvResponse({ 'X-Exported-Count': '3' })));
    vi.stubGlobal('fetch', fetchMock);

    const result = await exportDetailsCsv({ sessionId: 'abc', filters: [] });

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('/api/dataanalysis/session/abc/details/export');
    expect(JSON.parse(init.body as string)).toEqual({ filters: [] });
    expect(result.total).toBe(3); // no X-Total-Count: assume everything was exported
    expect(downloadedAs).toBe('export.csv');
  });

  it('reports a total above the exported count when the server cut the file off', async () => {
    vi.stubGlobal('fetch', () =>
      Promise.resolve(csvResponse({ 'X-Exported-Count': '100000', 'X-Total-Count': '123456' })),
    );

    const result = await exportDetailsCsv({});

    expect(result.exported).toBe(100000);
    expect(result.total).toBe(123456);
  });

  it("throws the API's error message and downloads nothing", async () => {
    vi.stubGlobal('fetch', () =>
      Promise.resolve(
        new Response(
          JSON.stringify({ error: { code: 1042901, message: 'Another export is running.' } }),
          { status: 429, headers: { 'Content-Type': 'application/json' } },
        ),
      ),
    );

    await expect(exportDetailsCsv({})).rejects.toThrow('Another export is running.');
    expect(click).not.toHaveBeenCalled();
  });

  it('says the server could not be reached when the request itself fails', async () => {
    vi.stubGlobal('fetch', () => Promise.reject(new TypeError('Failed to fetch')));

    await expect(exportDetailsCsv({})).rejects.toThrow(NETWORK_ERROR_MESSAGE);
  });
});

describe('filenameFromDisposition', () => {
  it('prefers the UTF-8 filename* form and decodes it', () => {
    expect(
      filenameFromDisposition(
        "attachment; filename=fallback.csv; filename*=UTF-8''%E9%8A%B7%E5%94%AE_2026.csv",
      ),
    ).toBe('銷售_2026.csv');
  });

  it('falls back to the plain filename', () => {
    expect(filenameFromDisposition('attachment; filename="sales.csv"')).toBe('sales.csv');
  });

  it('is undefined without a header', () => {
    expect(filenameFromDisposition(null)).toBeUndefined();
  });
});
