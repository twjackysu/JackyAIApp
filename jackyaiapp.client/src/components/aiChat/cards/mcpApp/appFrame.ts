/**
 * The frame an MCP App is drawn in (docs/ai-native/design/09 §4, D-201): one `sandbox="allow-scripts"` iframe with no
 * `allow-same-origin`, so the app's origin is opaque — it cannot read the site's cookies or storage, nor call its API —
 * and a Content-Security-Policy written before anything of the app's.
 */

import { HOST_CONTEXT_CHANGED } from './appBridge';

/** The iframe's sandbox: scripts only (no same origin, popups, forms, top navigation or modals). */
export const APP_SANDBOX = 'allow-scripts';

/** The tallest an app may make itself (`ui/notifications/size-changed`), and the shortest. */
export const APP_MAX_HEIGHT = 640;
export const APP_MIN_HEIGHT = 80;

/** What the frame is before an app says its size. */
export const APP_START_HEIGHT = 240;

/** The origins an app may name, as the server kept them (`McpToolClient.HttpsOrigins`); checked again here. */
export function httpsOrigins(domains: readonly unknown[] | undefined): string[] {
  const kept: string[] = [];
  for (const d of domains ?? []) {
    if (typeof d !== 'string') continue;
    // A name on the internet: no address literal, localhost or one-label name the reader's browser would reach at home.
    const host = /^https:\/\/((?:[a-z0-9-]+\.)+[a-z][a-z0-9-]*)(?::\d{1,5})?$/.exec(d)?.[1];
    if (!host || /\.(local|localhost)$/.test(host)) continue;
    if (!kept.includes(d)) kept.push(d);
    if (kept.length >= 20) break;
  }
  return kept;
}

/**
 * The app's CSP: the spec's default (no outside origin at all, inline script and style, `data:` images) with the https
 * origins its server declared added where it declared them; no frames, plugins, forms or `<base>`.
 */
export function appCsp(
  connectDomains?: readonly unknown[],
  resourceDomains?: readonly unknown[],
): string {
  const connect = httpsOrigins(connectDomains);
  const resources = httpsOrigins(resourceDomains);
  const plus = (base: string, extra: string[]) => [base, ...extra].join(' ');
  return [
    "default-src 'none'",
    plus("script-src 'unsafe-inline'", resources),
    plus("style-src 'unsafe-inline'", resources),
    plus('img-src data:', resources),
    plus('font-src data:', resources),
    plus('media-src data:', resources),
    connect.length ? `connect-src ${connect.join(' ')}` : "connect-src 'none'",
    "frame-src 'none'",
    "object-src 'none'",
    "base-uri 'none'",
    "form-action 'none'",
  ].join('; ');
}

const escapeAttribute = (s: string) =>
  s.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

/**
 * Our one script in the app's document, before anything of the app's: on the host's `host-context-changed` it sets the
 * document's `color-scheme` to the new theme, so the app keeps its state through a change of light or dark and its
 * background stays transparent (a browser paints a frame whose scheme differs from its element's). It only touches that
 * `<meta>`; the app gets the same notification and may restyle itself.
 */
export const SCHEME_SCRIPT =
  '<script>addEventListener("message",function(e){var d=e.data,t=d&&d.params&&d.params.theme;' +
  `if(e.source!==parent||(t!=="light"&&t!=="dark")||d.method!=="${HOST_CONTEXT_CHANGED}")return;` +
  'var m=document.querySelector(\'meta[name="color-scheme"]\');if(m)m.setAttribute("content",t)})</script>';

/**
 * The iframe's `srcdoc`: our doctype and CSP first, then the app's document without its own doctype. The parser puts
 * the `<meta>` in the head before any of the app's markup, so nothing of the app's — a comment, a fake `<head>` — can
 * come before it, and a CSP of the app's own only adds to ours (policies are all enforced). `scheme` is the light or
 * dark it starts in; a later change is a notification, never a new document (`SCHEME_SCRIPT`).
 */
export function appSrcDoc(html: string, csp: string, scheme: 'light' | 'dark' = 'light'): string {
  const body = html.replace(/^\ufeff?\s*<!doctype[^>]*>/i, '');
  return (
    '<!doctype html>' +
    `<meta http-equiv="Content-Security-Policy" content="${escapeAttribute(csp)}">` +
    '<meta name="referrer" content="no-referrer">' +
    // The same light or dark as the frame's element (its color-scheme): with the schemes the same, the browser leaves the
    // app's background transparent rather than painting it white under a dark chat.
    `<meta name="color-scheme" content="${scheme}">` +
    SCHEME_SCRIPT +
    body
  );
}

/** The height the frame takes for an app's `size-changed`, within the limits; null for a value that is not one. */
export function frameHeight(height: unknown): number | null {
  if (typeof height !== 'number' || !Number.isFinite(height)) return null;
  return Math.round(Math.min(APP_MAX_HEIGHT, Math.max(APP_MIN_HEIGHT, height)));
}
