/**
 * markdown 的 fenced code block 在 hast 裡固定是 `pre > code`，行內 code 則是單獨的 `code`。
 *
 * 這個模組存在的理由：react-markdown v9 之後不再傳 `inline` prop，而 `code` renderer 拿到的
 * node 其 `tagName` 永遠是 `'code'`（行內與區塊都一樣），所以在 `code` renderer 裡分辨不出
 * 兩者 —— 先前 `node.tagName !== 'pre'` 這個判斷恆為 true，區塊樣式整段是死碼。
 *
 * 與其修那個判斷，不如讓結構決定：由 `pre` renderer 直接從自己的 node 取出程式碼內容，
 * 區塊就完全由它負責；`code` renderer 因此只會被行內 code 呼叫到，無從判斷錯誤。
 */

/** hast 節點只取這裡用得到的欄位，避免為了型別把整包 hast 拉進來。 */
interface HastNode {
  type?: string;
  tagName?: string;
  value?: string;
  properties?: { className?: unknown };
  children?: HastNode[];
}

export interface FencedCode {
  /** 原始程式碼文字，未經 escape。 */
  text: string;
  /** `code` 元素上的 class（例如 `language-json`），沒有標語言時為 undefined。 */
  className: string | undefined;
}

function classNameOf(node: HastNode): string | undefined {
  const raw = node.properties?.className;
  if (typeof raw === 'string') return raw || undefined;
  if (Array.isArray(raw)) {
    const joined = raw.filter((c): c is string => typeof c === 'string').join(' ');
    return joined || undefined;
  }
  return undefined;
}

/**
 * 從 `pre` 的 hast 節點取出它包住的 fenced code。
 *
 * 回傳 null 代表這個 `pre` 不是 markdown 的 fenced block（理論上不會發生，因為沒有啟用
 * rehype-raw，所有 `pre` 都來自 fenced block）—— 呼叫端應改為原樣輸出 children，而不是
 * 顯示空白。
 */
export function readFencedCode(node: HastNode | undefined): FencedCode | null {
  const code = node?.children?.find((c) => c.type === 'element' && c.tagName === 'code');
  if (!code) return null;

  // fenced block 的內容一定是單一 text 節點；用 join 而非 [0] 是為了在 remark 因外掛而
  // 切成多段時仍然完整，不會只顯示第一段。
  const text = (code.children ?? [])
    .filter((c) => c.type === 'text' && typeof c.value === 'string')
    .map((c) => c.value as string)
    .join('');

  return { text, className: classNameOf(code) };
}
