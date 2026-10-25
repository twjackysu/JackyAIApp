import { useCallback, useRef, useState } from 'react';

/** A message an assistant's owner hands its panel to send as if typed (`AiAssistantPanel`'s `ask`). */
export interface ChatAsk {
  /** Tells the panel one ask from the next: each id is sent once. */
  id: number;
  text: string;
  /** As the first message of a new conversation. */
  fresh?: boolean;
  /** Quotes text the reader selected on a page (someone else's words, maybe). */
  quotesPageText?: boolean;
}

/**
 * The messages an assistant's owner (Studio AI, an app's `AiAssistantLauncher`) has been asked to send, in order,
 * kept until the panel says it sent each. Two asks before the panel has mounted (it is lazy-loaded), or in one
 * render, both go: the panel takes the first, and the next once that one is sent (queued behind its answer, as a
 * typed message would be). Holding only the last would drop the others.
 */
export function useAskQueue() {
  const [asks, setAsks] = useState<ChatAsk[]>([]);
  const lastId = useRef(0);
  const enqueue = useCallback((asked: Omit<ChatAsk, 'id'>) => {
    lastId.current += 1;
    const id = lastId.current;
    setAsks((queued) => [...queued, { ...asked, id }]);
  }, []);
  const sent = useCallback(
    (id: number) => setAsks((queued) => queued.filter((ask) => ask.id !== id)),
    [],
  );
  return { ask: asks[0] as ChatAsk | undefined, enqueue, sent };
}
