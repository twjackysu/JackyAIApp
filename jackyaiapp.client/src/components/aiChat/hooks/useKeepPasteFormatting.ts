import { useState } from 'react';

const KEEP_FORMATTING_KEY = 'aiAssistant.keepPasteFormatting';

const readKeepFormatting = () => {
  try {
    return localStorage.getItem(KEEP_FORMATTING_KEY) === '1';
  } catch {
    return false;
  }
};

/** Whether pasted formatted text keeps its formatting (as Markdown); remembered in this browser. */
export function useKeepPasteFormatting() {
  const [keepFormatting, setKeepFormatting] = useState(readKeepFormatting);
  const change = (keep: boolean) => {
    setKeepFormatting(keep);
    try {
      localStorage.setItem(KEEP_FORMATTING_KEY, keep ? '1' : '0');
    } catch {
      // Storage unavailable (private window, blocked site data): the choice lasts this visit only.
    }
  };
  return [keepFormatting, change] as const;
}
