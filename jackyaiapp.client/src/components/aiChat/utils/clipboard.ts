/** Copies text (throwing when it could not), falling back to a hidden textarea where the clipboard API is not available. */
export const copyText = async (text: string) => {
  try {
    await navigator.clipboard.writeText(text);
    return;
  } catch {
    // Not a secure context, or no permission: the old way below still works in most browsers.
  }
  const area = document.createElement('textarea');
  area.value = text;
  area.setAttribute('readonly', '');
  area.style.position = 'fixed';
  area.style.opacity = '0';
  document.body.appendChild(area);
  try {
    area.select();
    // It says whether it copied rather than throwing: a copy that did nothing must not be reported as done.
    if (!document.execCommand('copy')) throw new Error('copy failed');
  } finally {
    area.remove();
  }
};

/**
 * Copies `html` for a rich paste (a document, a mail) with `text` beside it for a place that takes text only. Throws when
 * the browser cannot put HTML on the clipboard (no `ClipboardItem`, not a secure context): there is no older way that
 * keeps the markup without putting it into the page.
 */
export const copyHtml = async (html: string, text: string) => {
  if (typeof ClipboardItem === 'undefined' || !navigator.clipboard?.write)
    throw new Error('rich copy unavailable');
  await navigator.clipboard.write([
    new ClipboardItem({
      'text/html': new Blob([html], { type: 'text/html' }),
      'text/plain': new Blob([text], { type: 'text/plain' }),
    }),
  ]);
};
