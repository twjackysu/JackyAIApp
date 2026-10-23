import { useCallback, useEffect, useState } from 'react';

import { ParsedBlock } from '@/hooks/useStreamingHtmlParser';

/**
 * The on-demand preview (HTML / Markdown) opened from a button inside a message. While it shows a
 * still-streaming HTML block, it follows the latest parsed content.
 */
export function usePreview(blocks: ParsedBlock[], currentHtmlBlock: ParsedBlock | null) {
  const [open, setOpen] = useState(false);
  const [block, setBlock] = useState<ParsedBlock | null>(null);

  // Guarded by content/completeness to avoid render loops.
  useEffect(() => {
    if (!open || !block || block.isComplete) return;
    const live = currentHtmlBlock ?? blocks.find((b) => b.contentType === 'html');
    if (live && (live.content !== block.content || live.isComplete !== block.isComplete)) {
      setBlock(live);
    }
  }, [currentHtmlBlock, blocks, open, block]);

  const show = useCallback((next: ParsedBlock) => {
    setBlock(next);
    setOpen(true);
  }, []);
  const close = useCallback(() => setOpen(false), []);

  return { open, block, show, close };
}
