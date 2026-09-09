import hljs from 'highlight.js/lib/core';
import bash from 'highlight.js/lib/languages/bash';
import css from 'highlight.js/lib/languages/css';
import javascript from 'highlight.js/lib/languages/javascript';
import json from 'highlight.js/lib/languages/json';
import markdown from 'highlight.js/lib/languages/markdown';
import plaintext from 'highlight.js/lib/languages/plaintext';
import python from 'highlight.js/lib/languages/python';
import sql from 'highlight.js/lib/languages/sql';
import typescript from 'highlight.js/lib/languages/typescript';
import xml from 'highlight.js/lib/languages/xml';
import yaml from 'highlight.js/lib/languages/yaml';

/**
 * 全站唯一的 highlight.js 設定。
 *
 * 一定要走 `highlight.js/lib/core` 並逐一註冊語言，不要 import 整包 `highlight.js`，
 * 也不要用 `rehype-highlight` 的預設值 —— 那些進入點會把 highlight.js 的 common 語言集
 * （約 37 種語言）整包打進 bundle。這裡登記的語言就是全站支援的語言，其餘語言的
 * code block 會原樣輸出（不上色），不會有錯誤。
 *
 * 想支援新語言：在下面 LANGUAGES 加一筆即可，不需要動任何呼叫端。
 */
const LANGUAGES: Record<string, Parameters<typeof hljs.registerLanguage>[1]> = {
  bash,
  css,
  javascript,
  json,
  markdown,
  plaintext,
  python,
  sql,
  typescript,
  xml,
  yaml,
};

for (const [name, language] of Object.entries(LANGUAGES)) {
  hljs.registerLanguage(name, language);
}

export { hljs };

/**
 * 從 markdown code block 的 class（react-markdown 會給 `language-xxx`）取出語言名稱。
 * 沒有標語言、或標了未註冊的語言時回傳 null。
 */
export function resolveLanguage(className: string | undefined): string | null {
  if (!className) return null;
  for (const token of className.split(/\s+/)) {
    const name = token.startsWith('language-')
      ? token.slice('language-'.length)
      : token.startsWith('lang-')
        ? token.slice('lang-'.length)
        : null;
    // getLanguage 也會比對語言自己宣告的 alias（例如 js → javascript、html → xml）。
    if (name && hljs.getLanguage(name)) return name;
  }
  return null;
}

/**
 * 把程式碼轉成已上色、且已做 HTML escape 的字串，供 dangerouslySetInnerHTML 使用。
 * 語言不支援或上色失敗時回傳 null，呼叫端應改為直接輸出純文字（交給 React escape）。
 */
export function highlightToHtml(code: string, className: string | undefined): string | null {
  const language = resolveLanguage(className);
  if (!language) return null;
  try {
    return hljs.highlight(code, { language }).value;
  } catch (error) {
    console.warn('Failed to highlight code:', error);
    return null;
  }
}
