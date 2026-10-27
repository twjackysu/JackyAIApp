/**
 * The key that takes the focus into the bar (D-188): the bar shows by itself and takes nothing, so without it a reader
 * who selected with the keyboard had no way to its buttons. `code`, not `key`: on a Mac, Option+A types 「å」.
 */
export const SELECTION_BAR_SHORTCUT = 'Alt+A';
export const isSelectionBarShortcut = (
  e: Pick<KeyboardEvent, 'altKey' | 'ctrlKey' | 'metaKey' | 'shiftKey' | 'code'>,
) => e.altKey && !e.ctrlKey && !e.metaKey && !e.shiftKey && e.code === 'KeyA';

/** What a screen reader hears when a selection settles: the bar itself is not where the focus is. */
export const selectionHint = (assistantName: string) =>
  `已選取文字，按 ${SELECTION_BAR_SHORTCUT} 用 ${assistantName} 處理`;
