/**
 * What reading an answer aloud says (`useSpeechOutput`): the answer's Markdown as plain words, cut into runs of
 * English and of Chinese so each is read in its own voice — an English passage in an English voice, its
 * translation in a Chinese one. Code, links' addresses, images and emoji are not read.
 */

export type SpeechLang = 'en-US' | 'zh-TW';

export interface SpeechSegment {
  text: string;
  lang: SpeechLang;
}

/** The most characters read from one answer (a long table would otherwise take minutes). */
export const MAX_SPOKEN_CHARS = 3000;

/** The answer's Markdown as the words a reader sees. */
export function plainTextOf(markdown: string): string {
  return (
    markdown
      // Fenced code and HTML blocks are not read.
      .replace(/```[\s\S]*?(```|$)/g, ' ')
      .replace(/<[^>]+>/g, ' ')
      // Images go, links keep their words.
      .replace(/!\[[^\]]*\]\([^)]*\)/g, ' ')
      .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
      .replace(/https?:\/\/\S+/g, ' ')
      // Inline code keeps its words; emphasis, headings, quotes, list marks and table rules go.
      .replace(/`([^`]*)`/g, '$1')
      .replace(/(\*\*|__|\*|~~)(?=\S)([\s\S]*?\S)\1/g, '$2')
      .replace(/^\s{0,3}(#{1,6}|>+|[-*+]|\d+[.)])\s+/gm, '')
      .replace(/^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$/gm, ' ')
      .replace(/\|/g, '，')
      .replace(/\p{Extended_Pictographic}|\uFE0F|\u200D/gu, '')
      .replace(/\s+/g, ' ')
      .trim()
  );
}

/** A letter of a language: Latin for English; Han, CJK punctuation or a full-width form for Chinese. */
function langOf(ch: string): SpeechLang | null {
  if (/[A-Za-z]/.test(ch)) return 'en-US';
  if (/[\p{Script=Han}\u3000-\u303f\uff00-\uffef]/u.test(ch)) return 'zh-TW';
  return null;
}

/**
 * The answer cut into runs of one language. Digits, spaces and ASCII punctuation stay in the run they are in
 * (「共 86 個字」 is read in Chinese, "in 2026." in English); text before the first letter goes with the first run.
 * A run with no letters in it is dropped.
 */
export function speechSegmentsOf(markdown: string, max = MAX_SPOKEN_CHARS): SpeechSegment[] {
  const text = plainTextOf(markdown).slice(0, max);
  const segments: SpeechSegment[] = [];
  let current = '';
  let lang: SpeechLang | null = null;
  for (const ch of text) {
    const own = langOf(ch);
    if (own && lang && own !== lang) {
      segments.push({ text: current, lang });
      current = '';
    }
    if (own) lang = own;
    current += ch;
  }
  if (lang) segments.push({ text: current, lang });
  return segments
    .map((s) => ({ ...s, text: s.text.trim() }))
    .filter((s) => /[A-Za-z\p{Script=Han}]/u.test(s.text));
}
