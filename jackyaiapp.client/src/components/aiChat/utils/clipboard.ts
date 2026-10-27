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
