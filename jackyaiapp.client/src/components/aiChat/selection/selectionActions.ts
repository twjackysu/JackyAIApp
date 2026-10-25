/**
 * Asking the page's assistant (Studio AI, or an app's own) about what the reader selected (docs/ai-native
 * DECISIONS D-086, D-089): select a
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

/**
 * What the bar offers for `text` (already `selectedTextOf`), in order. An assistant that cannot add a word to the
 * repository (`canSaveWord: false`, an app's own) explains a word instead: on /finance "ETF" is a term to explain.
 */
export function selectionActionsFor(
  text: string,
  { canSaveWord = true }: { canSaveWord?: boolean } = {},
): SelectionAction[] {
  if (selectedWordOf(text)) {
    return canSaveWord
      ? [
          { kind: 'dictionary', label: '查字典' },
          { kind: 'save-word', label: '加入單字庫' },
          { kind: 'ask', label: '問 AI' },
        ]
      : [
          { kind: 'explain', label: '解釋' },
          { kind: 'dictionary', label: '查字典' },
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

/**
 * What pressing an action does, the same for every assistant (D-086, D-089): one English word's dictionary page is
 * a page to go to (no AI); 問 AI puts the text in the box after what was typed, for the reader to finish; the rest
 * are sent at once. Text from the page goes as a new conversation (it stands on its own) and counts as outside text
 * (`quotesPageText`: it may be someone else's words, so the conversation's writes ask first); text from one of the
 * assistant's own answers continues the conversation it belongs to. Null when there is nothing to do.
 */
export type SelectionStep =
  | { do: 'go'; path: string }
  | { do: 'draft'; text: string; quotesPageText: boolean }
  | { do: 'send'; text: string; fromPage: boolean };

export function selectionStepOf(
  kind: SelectionActionKind,
  text: string,
  inAssistant: boolean,
): SelectionStep | null {
  if (kind === 'dictionary') {
    const path = dictionaryPathOf(text);
    return path ? { do: 'go', path } : null;
  }
  if (kind === 'ask') {
    return { do: 'draft', text: selectionMessage('ask', text), quotesPageText: !inAssistant };
  }
  return { do: 'send', text: selectionMessage(kind, text), fromPage: !inAssistant };
}

/** The box after 問 AI: what was typed, then the selection's opening, never in place of it. */
export const draftWithSelection = (typed: string, opening: string): string =>
  [typed.trimEnd(), opening].filter(Boolean).join('\n');
