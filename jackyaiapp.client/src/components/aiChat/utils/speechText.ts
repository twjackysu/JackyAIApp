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
      .replace(/<\/?[A-Za-z][^>]*>/g, ' ')
      // Images go, links keep their words.
      .replace(/!\[[^\]]*\]\([^)]*\)/g, ' ')
      .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
      .replace(/https?:\/\/\S+/g, ' ')
      // Inline code keeps its words; emphasis, headings, quotes, list marks and table rules go.
      .replace(/`([^`]*)`/g, '$1')
      .replace(/(\*\*|__|~~)(?=\S)([\s\S]*?\S)\1/g, '$2')
      .replace(/(^|[^\w*])\*(?=\S)([^*\n]*?\S)\*(?![\w*])/g, '$1$2')
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
  const kept = segments
    .map((s) => ({ ...s, text: s.text.trim() }))
    .filter((s) => /[A-Za-z\p{Script=Han}]/u.test(s.text));
  return chunked(withShortLatinInChinese(kept));
}

/** The longest Latin run read in the Chinese voice when it sits in Chinese text (ETF, EPS, AI). */
const SHORT_LATIN = 3;

/**
 * An abbreviation in a Chinese sentence stays in the Chinese voice, which reads it letter by letter anyway:
 * switching voices for 「ETF」 would break the sentence into pieces. Runs of one language are then joined.
 */
function withShortLatinInChinese(segments: SpeechSegment[]): SpeechSegment[] {
  const relabelled = segments.map((s, i): SpeechSegment => {
    const short = s.lang === 'en-US' && (s.text.match(/[A-Za-z]/g)?.length ?? 0) <= SHORT_LATIN;
    const nearChinese = segments[i - 1]?.lang === 'zh-TW' || segments[i + 1]?.lang === 'zh-TW';
    return short && nearChinese ? { ...s, lang: 'zh-TW' } : s;
  });
  const joined: SpeechSegment[] = [];
  for (const s of relabelled) {
    const last = joined[joined.length - 1];
    if (last?.lang === s.lang) last.text += ` ${s.text}`;
    else joined.push({ ...s });
  }
  return joined;
}

/** The most characters one utterance holds: some browsers' voices stop a long one partway, without a word. */
export const MAX_UTTERANCE_CHARS = 200;

/** Each run cut at its sentence ends into pieces no longer than `MAX_UTTERANCE_CHARS` (a hard cut if need be). */
function chunked(segments: SpeechSegment[]): SpeechSegment[] {
  return segments.flatMap((s) => {
    const sentences = s.text.match(/[^.!?。！？；;]+[.!?。！？；;]*\s*/g) ?? [s.text];
    const pieces: string[] = [];
    let current = '';
    for (const sentence of sentences) {
      if (current && current.length + sentence.length > MAX_UTTERANCE_CHARS) {
        pieces.push(current);
        current = '';
      }
      current += sentence;
      while (current.length > MAX_UTTERANCE_CHARS) {
        pieces.push(current.slice(0, MAX_UTTERANCE_CHARS));
        current = current.slice(MAX_UTTERANCE_CHARS);
      }
    }
    if (current) pieces.push(current);
    return pieces
      .map((t) => t.trim())
      .filter(Boolean)
      .map((text) => ({ text, lang: s.lang }));
  });
}
