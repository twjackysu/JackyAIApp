import type { Message } from '../types';

/**
 * Questions offered under an answer to carry on from it (docs/ai-native DECISIONS D-062). Decided
 * here, from the tools the answer used, not asked of the model: they cost nothing, cannot be steered
 * by what a tool read, and are the same every time for the same kind of answer.
 */
export interface FollowUpRule {
  /** Offered after an answer that used one of these tools (by the server's name); every answer when not set. */
  tools?: readonly string[];
  texts: readonly string[];
}

/** At most this many under one answer, so they stay one row on a phone. */
export const MAX_FOLLOW_UPS = 3;

/**
 * What to offer under `answer`: only a finished answer (not one stopped, failed or reopened from the
 * history, whose tools are not kept), the rules of the tools it used first, in the config's order,
 * then those for every answer; never what the reader just asked, never twice.
 */
export function followUpsFor(
  answer: Message,
  asked: string | undefined,
  rules: readonly FollowUpRule[] | undefined,
): string[] {
  if (!rules?.length || answer.isUser || !answer.answered) return [];
  const used = new Set(
    (answer.toolSteps ?? []).filter((s) => s.status === 'done').map((s) => s.name),
  );
  const matching = rules.filter((r) => r.tools?.some((t) => used.has(t)));
  const general = rules.filter((r) => !r.tools);
  const picked: string[] = [];
  for (const text of [...matching, ...general].flatMap((r) => r.texts)) {
    if (picked.length === MAX_FOLLOW_UPS) break;
    if (text !== asked?.trim() && !picked.includes(text)) picked.push(text);
  }
  return picked;
}
