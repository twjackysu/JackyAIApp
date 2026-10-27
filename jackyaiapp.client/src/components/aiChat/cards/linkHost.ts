/**
 * The host an outside link goes to, as the reader should judge it: the card's title and description are a
 * tool's words, which may name one site and point to another (D-160). `URL` gives a lookalike name in its
 * punycode form (xn--…), so it cannot pass for the real one. Null for this site's own pages.
 */
export function linkHostOf(href: string): string | null {
  if (href.startsWith('/')) return null;
  try {
    return new URL(href).host || null;
  } catch {
    return null;
  }
}
