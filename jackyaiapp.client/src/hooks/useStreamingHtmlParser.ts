import { useMemo } from 'react';

export interface ParsedBlock {
  id: string;
  contentType: 'markdown' | 'html';
  content: string;
  isComplete?: boolean;
}

interface ParseState {
  mode: 'markdown' | 'html' | 'codeblock';
  tagStack: string[];
  currentBlockId: string | null;
  currentContent: string;
  blockCounter: number;
}

interface HtmlParserResult {
  blocks: ParsedBlock[];
  currentHtmlBlock: ParsedBlock | null;
  isStreamingHtml: boolean;
}

// HTML 標籤識別正則表達式（移除 g 標誌避免狀態記憶問題）
const HTML_START_TAG = /<([a-zA-Z][a-zA-Z0-9]*)\b[^>]*>/;
const HTML_END_TAG = /<\/([a-zA-Z][a-zA-Z0-9]*)\s*>/;
// const HTML_SELF_CLOSING = /<([a-zA-Z][a-zA-Z0-9]*)\b[^>]*\/>/;
const CODE_BLOCK_START = /```html\s*/;
const CODE_BLOCK_END = /```/;

// 自閉合標籤列表
const SELF_CLOSING_TAGS = new Set([
  'area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input',
  'link', 'meta', 'param', 'source', 'track', 'wbr'
]);

export function useStreamingHtmlParser(streamingContent: string): HtmlParserResult {

  const result = useMemo(() => {
    const newBlocks: ParsedBlock[] = [];
    let currentState: ParseState = {
      mode: 'markdown',
      tagStack: [],
      currentBlockId: null,
      currentContent: '',
      blockCounter: 0
    };

    let i = 0;
    let markdownBuffer = '';

    while (i < streamingContent.length) {
      const remaining = streamingContent.slice(i);

      if (currentState.mode === 'markdown') {
        // 檢查是否遇到 ```html
        const codeBlockMatch = remaining.match(CODE_BLOCK_START);
        if (codeBlockMatch && codeBlockMatch.index === 0) {
          // 保存之前的 markdown 內容
          if (markdownBuffer.trim()) {
            newBlocks.push({
              id: `markdown-${currentState.blockCounter++}`,
              contentType: 'markdown',
              content: markdownBuffer.trim(),
              isComplete: true
            });
            markdownBuffer = '';
          }

          // 切換到 codeblock 模式
          currentState.mode = 'codeblock';
          currentState.currentBlockId = `html-${currentState.blockCounter++}`;
          currentState.currentContent = '';
          i += codeBlockMatch[0].length;
          
          // 跳過緊接著的換行符（如果有的話）
          if (i < streamingContent.length && streamingContent[i] === '\n') {
            i++;
          }
          continue;
        }

        // 檢查是否遇到 HTML 標籤
        const htmlMatch = remaining.match(HTML_START_TAG);
        if (htmlMatch && htmlMatch.index === 0) {
          const tagName = htmlMatch[1].toLowerCase();
          
          // 保存之前的 markdown 內容
          if (markdownBuffer.trim()) {
            newBlocks.push({
              id: `markdown-${currentState.blockCounter++}`,
              contentType: 'markdown',
              content: markdownBuffer.trim(),
              isComplete: true
            });
            markdownBuffer = '';
          }

          // 切換到 html 模式
          currentState.mode = 'html';
          currentState.currentBlockId = `html-${currentState.blockCounter++}`;
          currentState.currentContent = htmlMatch[0];
          
          // 如果不是自閉合標籤，加入標籤堆棧
          if (!SELF_CLOSING_TAGS.has(tagName)) {
            currentState.tagStack = [tagName];
          }
          
          i += htmlMatch[0].length;
          continue;
        }

        // 普通 markdown 字符
        markdownBuffer += streamingContent[i];
        i++;
      } 
      else if (currentState.mode === 'codeblock') {
        // 檢查是否遇到結束的 ```
        const endMatch = remaining.match(CODE_BLOCK_END);
        if (endMatch && endMatch.index === 0) {
          // HTML 區塊完成
          newBlocks.push({
            id: currentState.currentBlockId!,
            contentType: 'html',
            content: currentState.currentContent,
            isComplete: true
          });

          // 重置狀態
          currentState.mode = 'markdown';
          currentState.currentBlockId = null;
          currentState.currentContent = '';
          i += endMatch[0].length;
          continue;
        }

        // 累積 HTML 內容
        currentState.currentContent += streamingContent[i];
        i++;
      }
      else if (currentState.mode === 'html') {
        // 檢查結束標籤
        const endMatch = remaining.match(HTML_END_TAG);
        if (endMatch && endMatch.index === 0) {
          const tagName = endMatch[1].toLowerCase();
          currentState.currentContent += endMatch[0];
          
          // 從堆棧中移除對應的標籤
          const lastTagIndex = currentState.tagStack.lastIndexOf(tagName);
          if (lastTagIndex !== -1) {
            currentState.tagStack = currentState.tagStack.slice(0, lastTagIndex);
          }

          i += endMatch[0].length;

          // 如果堆棧為空，HTML 區塊完成
          if (currentState.tagStack.length === 0) {
            newBlocks.push({
              id: currentState.currentBlockId!,
              contentType: 'html',
              content: currentState.currentContent,
              isComplete: true
            });

            currentState.mode = 'markdown';
            currentState.currentBlockId = null;
            currentState.currentContent = '';
          }
          continue;
        }

        // 檢查嵌套的開始標籤
        const startMatch = remaining.match(HTML_START_TAG);
        if (startMatch && startMatch.index === 0) {
          const tagName = startMatch[1].toLowerCase();
          currentState.currentContent += startMatch[0];
          
          // 如果不是自閉合標籤，加入堆棧
          if (!SELF_CLOSING_TAGS.has(tagName)) {
            currentState.tagStack.push(tagName);
          }
          
          i += startMatch[0].length;
          continue;
        }

        // 普通字符
        currentState.currentContent += streamingContent[i];
        i++;
      }
    }

    // 處理剩餘內容
    if (markdownBuffer.trim() && currentState.mode === 'markdown') {
      newBlocks.push({
        id: `markdown-${currentState.blockCounter++}`,
        contentType: 'markdown',
        content: markdownBuffer.trim(),
        isComplete: true
      });
    }

    // 如果還在解析 HTML，添加未完成的區塊
    if (currentState.currentBlockId && currentState.currentContent) {
      newBlocks.push({
        id: currentState.currentBlockId,
        contentType: 'html',
        content: currentState.currentContent,
        isComplete: false
      });
    }

    // 不需要更新內部狀態，因為我們使用 useMemo 來重新計算

    // 找到當前正在 streaming 的 HTML 區塊
    const currentHtmlBlock = newBlocks.find(block => 
      block.contentType === 'html' && !block.isComplete
    ) || null;

    return {
      blocks: newBlocks,
      currentHtmlBlock,
      isStreamingHtml: currentState.mode === 'html' || currentState.mode === 'codeblock'
    };
  }, [streamingContent]);

  return result;
}

export default useStreamingHtmlParser;