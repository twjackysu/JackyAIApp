import { getApiErrorMessage } from '../utils/errorMessage';

import type { DetailsRequest } from './types';
import type { FetchBaseQueryError } from '@reduxjs/toolkit/query';

export interface DetailsExportResult {
  /** Rows in the downloaded file. */
  exported: number;
  /** Rows matching the filters; more than `exported` when the server's export cap cut it off. */
  total: number;
  filename: string;
}

/**
 * Downloads every row matching a Details request as CSV — no paging; the server caps it and
 * reports the real total in X-Total-Count. A plain fetch rather than an RTK Query endpoint:
 * the body is a file, not the API's JSON envelope.
 */
export async function exportDetailsCsv(
  request: Omit<DetailsRequest, 'skip' | 'take'> & { sessionId?: string },
): Promise<DetailsExportResult> {
  const { sessionId, ...body } = request;
  const url = sessionId
    ? `/api/dataanalysis/session/${sessionId}/details/export`
    : '/api/dataanalysis/details/export';

  let res: Response;
  try {
    res = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
  } catch (e) {
    throw new Error(getApiErrorMessage({ status: 'FETCH_ERROR', error: String(e) }));
  }

  if (!res.ok) {
    const text = await res.text();
    let data: unknown = text;
    try {
      data = JSON.parse(text);
    } catch {
      // not JSON — getApiErrorMessage handles plain text too
    }
    throw new Error(getApiErrorMessage({ status: res.status, data } as FetchBaseQueryError));
  }

  const blob = await res.blob();
  const filename = filenameFromDisposition(res.headers.get('Content-Disposition')) ?? 'export.csv';
  downloadBlob(blob, filename);

  const exported = Number(res.headers.get('X-Exported-Count') ?? 0);
  const total = Number(res.headers.get('X-Total-Count') ?? exported);
  return { exported, total, filename };
}

/** Reads `filename*=UTF-8''…` (preferred) or `filename="…"` from a Content-Disposition header. */
export function filenameFromDisposition(header: string | null): string | undefined {
  if (!header) return undefined;
  const star = /filename\*\s*=\s*UTF-8''([^;]+)/i.exec(header);
  if (star) {
    try {
      return decodeURIComponent(star[1].trim());
    } catch {
      // malformed escape — fall through to the plain form
    }
  }
  const plain = /filename\s*=\s*"?([^";]+)"?/i.exec(header);
  return plain?.[1].trim();
}

function downloadBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}
