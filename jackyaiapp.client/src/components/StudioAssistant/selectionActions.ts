/**
 * Asking Studio AI about what the reader selected on a page (docs/ai-native DECISIONS D-086): select a
 * sentence of an example, a paragraph of an analysis, a word in a passage, and a small bar over it offers
 * to explain it, translate it, or ask about it — or, for one English word, to open its dictionary page
 * (no AI) or add it to the repository (the AI's `add_word_to_repository`, which asks first). What is sent
 * is what the reader would have pasted: their own message, nothing a tool read.
 */

export type SelectionActionKind = 'explain' | 'translate' | 'ask' | 'dictionary' | 'save-word';

export interface SelectionAction {
  kind: SelectionActionKind;
  label: string;
}

/** The most of a selection that goes into a message; longer is cut, with an ellipsis. */
export const SELECTION_MAX = 800;

/** One English word as the dictionary takes it (letters, an apostrophe or a hyphen). */
const ENGLISH_WORD = /^[a-z][a-z'-]{1,29}$/i;

/**
 * The selection as it goes into a message — runs of spaces and line breaks made one space, trimmed, cut at
 * `SELECTION_MAX` — or null for nothing worth asking about (fewer than two characters).
 */
export function selectedTextOf(raw: string): string | null {
  const text = raw.replace(/\s+/g, ' ').trim();
  if ([...text].length < 2) return null;
  const chars = [...text];
  return chars.length > SELECTION_MAX
    ? `${chars.slice(0, SELECTION_MAX).join('').trimEnd()}…`
    : text;
}

/** The selection as one English word, lowercase, or undefined. */
export function selectedWordOf(text: string): string | undefined {
  const word = text
    .trim()
    .replace(/[.,;:!?"”“)(]+$/u, '')
    .replace(/^[("“]+/u, '');
  return ENGLISH_WORD.test(word) && !/['-]$/.test(word) ? word.toLowerCase() : undefined;
}

/** Whether the selection is mostly English (so it is translated into Chinese, else into English). */
export function isMostlyEnglish(text: string): boolean {
  const latin = text.match(/[a-z]/gi)?.length ?? 0;
  const cjk = text.match(/[\u3400-\u9fff\uf900-\ufaff]/g)?.length ?? 0;
  return latin > cjk * 3;
}

/** What the bar offers for `text` (already `selectedTextOf`), in order. */
export function selectionActionsFor(text: string): SelectionAction[] {
  if (selectedWordOf(text)) {
    return [
      { kind: 'dictionary', label: '查字典' },
      { kind: 'save-word', label: '加入單字庫' },
      { kind: 'ask', label: '問 AI' },
    ];
  }
  return [
    { kind: 'explain', label: '解釋' },
    { kind: 'translate', label: isMostlyEnglish(text) ? '翻成中文' : '翻成英文' },
    { kind: 'ask', label: '問 AI' },
  ];
}

const quote = (text: string) => `「${text}」`;

/**
 * The message an action sends (explain, translate, save-word), or for `ask` what goes into the box for the
 * reader to finish. `dictionary` sends nothing; its page is `dictionaryPathOf`.
 */
export function selectionMessage(
  kind: Exclude<SelectionActionKind, 'dictionary'>,
  text: string,
): string {
  switch (kind) {
    case 'explain':
      return `請解釋我在這一頁選取的這段：\n\n${quote(text)}`;
    case 'translate':
      return isMostlyEnglish(text)
        ? `把這段翻成自然的繁體中文，再列出裡面值得學的單字或片語：\n\n${quote(text)}`
        : `把這段翻成自然的英文：\n\n${quote(text)}`;
    case 'save-word':
      return `把「${selectedWordOf(text) ?? text}」加入我的單字庫`;
    case 'ask':
      return `關於${quote(text)}，`;
  }
}

/** The dictionary page of the selected word, or undefined when it is not one English word. */
export function dictionaryPathOf(text: string): string | undefined {
  const word = selectedWordOf(text);
  return word ? `/english/dictionary/${encodeURIComponent(word)}` : undefined;
}
