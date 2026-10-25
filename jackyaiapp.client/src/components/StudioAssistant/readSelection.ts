import { selectedTextOf } from './selectionActions';

/** Studio AI's own drawer: what its answers say can be asked about too, in the same conversation. */
export const STUDIO_DRAWER = '[data-studio-drawer]';
/** Text that is never asked about: a box the reader types in (Studio AI's own included). */
const NEVER =
  'input, textarea, [contenteditable=""], [contenteditable="true"], [data-no-selection-ask]';
/** Outside Studio AI's drawer, what is not the page's text: a dialog, a drawer, a menu over the page. */
const NOT_PAGE_TEXT = '[role="dialog"], .MuiModal-root, .MuiDrawer-root';

/** How long the selection must stay put before the bar shows (a drag still selecting is not asked about). */
export const SELECTION_SETTLE_MS = 250;

export interface Selected {
  text: string;
  /** Where the bar goes, in the viewport. */
  left: number;
  top: number;
  below: boolean;
  /** In one of Studio AI's answers, not on the page under it. */
  inStudio: boolean;
}

const elementOf = (node: Node | null): Element | null =>
  node ? (node.nodeType === Node.ELEMENT_NODE ? (node as Element) : node.parentElement) : null;

/** The text the reader has selected now — on the page, or in Studio AI's answers — and where it is, or null. */
export function readSelection(): Selected | null {
  const selection = window.getSelection?.();
  if (!selection || selection.isCollapsed || selection.rangeCount === 0) return null;
  const range = selection.getRangeAt(0);
  const elements = [selection.anchorNode, selection.focusNode, range.commonAncestorContainer].map(
    elementOf,
  );
  if (elements.some((element) => !element || element.closest(NEVER))) return null;
  const inStudio = elements.map((element) => !!element!.closest(STUDIO_DRAWER));
  // Both ends in the drawer, or both on the page; on the page, not in something laid over it.
  if (inStudio.some((v) => v !== inStudio[0])) return null;
  if (!inStudio[0] && elements.some((element) => element!.closest(NOT_PAGE_TEXT))) return null;
  const text = selectedTextOf(selection.toString());
  if (!text || typeof range.getBoundingClientRect !== 'function') return null;
  const rect = range.getBoundingClientRect();
  if (rect.width === 0 && rect.height === 0) return null;
  // On a touch screen the browser's own menu sits over the selection, so the bar goes under it; with a
  // mouse, over it, unless there is no room above.
  const coarse = window.matchMedia?.('(pointer: coarse)').matches ?? false;
  const below = coarse || rect.top < 56;
  const half = 130;
  const left = Math.min(
    Math.max(rect.left + rect.width / 2, half + 8),
    Math.max(window.innerWidth - half - 8, half + 8),
  );
  return {
    text,
    left,
    top: below ? rect.bottom + (coarse ? 36 : 8) : rect.top - 8,
    below,
    inStudio: inStudio[0],
  };
}
