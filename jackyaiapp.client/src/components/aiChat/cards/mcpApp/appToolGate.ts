/**
 * Which of its server's tools an MCP App's card lets it run (D-207). A tool that only reads and runs on its own needs
 * nothing; a tool for the app alone that may change something (the official system-monitor app polls one with no
 * `readOnlyHint`) runs only once the reader allows it here — the server answers `AppCallNeedsReader` until then and is
 * the one that decides which tools may ask at all. What the reader decides lasts as long as the card is on the page:
 * nothing is stored, so an old answer's app asks again. A refused tool can be allowed after all (改為允許), and an
 * allowed one pauses while its card is out of sight (D-209).
 */

import { ErrorCodes } from '@/apis/types';

type JsonRpcId = string | number;

/** A call of the app's that waits on the reader. */
export interface WaitingCall {
  id: JsonRpcId;
  name: string;
  arguments: Record<string, unknown>;
}

export interface ToolGate {
  /** Tools the reader allowed on this card: their calls go on without asking. */
  allowed: readonly string[];
  /** Tools the reader said no to: their calls are refused here, without asking again. */
  refused: readonly string[];
  /** Calls waiting on the reader, oldest first; the first one's tool is the one being asked about. */
  waiting: readonly WaitingCall[];
}

export const OPEN_GATE: ToolGate = { allowed: [], refused: [], waiting: [] };

/** The most calls held while the reader decides; an app polling every few seconds is told to wait beyond them. */
export const MAX_WAITING_CALLS = 3;

/** What the app is told when the reader said no, or while too many of its calls wait. */
export const REFUSED_MESSAGE = 'The user did not allow this tool.';
export const BUSY_MESSAGE = 'Waiting for the user to allow this tool.';
/** What the app is told when it calls a tool the reader allowed while its card is out of sight (scrolled away, or the
 * tab hidden): what was allowed was the app the reader is looking at, so a polling app costs nothing unseen. */
export const PAUSED_MESSAGE =
  'Paused while the app is not on screen; calls go on when the user sees it again.';

export const isAllowed = (gate: ToolGate, name: string) => gate.allowed.includes(name);
export const isRefused = (gate: ToolGate, name: string) => gate.refused.includes(name);

/** The tool the reader is being asked about, or null. */
export const askingAbout = (gate: ToolGate): string | null => gate.waiting[0]?.name ?? null;

/** Whether the server said this call waits on the reader (rather than that it may never run). */
export function needsReader(error: unknown): boolean {
  if (typeof error !== 'object' || error === null || !('data' in error)) return false;
  const data = (error as { data: unknown }).data;
  return (
    typeof data === 'object' &&
    data !== null &&
    (data as { error?: { code?: unknown } }).error?.code === ErrorCodes.AppCallNeedsReader
  );
}

/**
 * The gate with `call` held for the reader, or null when as many calls as are held already wait, or the reader is being
 * asked about another tool: one tool at a time, so a second press can never allow a tool queued behind the one read.
 */
export function hold(gate: ToolGate, call: WaitingCall): ToolGate | null {
  if (gate.waiting.length >= MAX_WAITING_CALLS) return null;
  if (gate.waiting.length > 0 && gate.waiting[0].name !== call.name) return null;
  return { ...gate, waiting: [...gate.waiting, call] };
}

/**
 * The reader's answer about `name`, the tool the card showed: that tool allowed or refused from now on, and its waiting
 * calls released — to run (`allow`) or to be refused. Nothing when the card is no longer asking about that tool.
 */
export function decide(
  gate: ToolGate,
  name: string,
  allow: boolean,
): { gate: ToolGate; released: WaitingCall[] } {
  if (askingAbout(gate) !== name) return { gate, released: [] };
  const released = gate.waiting.filter((c) => c.name === name);
  const without = (list: readonly string[]) => list.filter((n) => n !== name);
  return {
    gate: {
      allowed: allow ? [...without(gate.allowed), name] : without(gate.allowed),
      refused: allow ? without(gate.refused) : [...without(gate.refused), name],
      waiting: gate.waiting.filter((c) => c.name !== name),
    },
    released,
  };
}

/** The gate once the app's document is drawn anew (light or dark): what the reader decided stays, the old calls go. */
export const forNewDocument = (gate: ToolGate): ToolGate =>
  gate.waiting.length ? { ...gate, waiting: [] } : gate;

/** How long after the question appears, or the app last changed its size, a press on it counts: the app cannot move
 * 允許 under a pointer already on its way to press something of the app's. */
export const PRESS_GUARD_MS = 600;

/** The reader takes back what they allowed: the tool's calls are refused from now on. */
export function stopAllowing(gate: ToolGate, name: string): ToolGate {
  return {
    ...gate,
    allowed: gate.allowed.filter((n) => n !== name),
    refused: isRefused(gate, name) ? gate.refused : [...gate.refused, name],
  };
}

/** The reader changes their mind about a tool they said no to, or stopped allowing: allowed from now on. */
export function allowAgain(gate: ToolGate, name: string): ToolGate {
  if (!isRefused(gate, name)) return gate;
  return {
    ...gate,
    allowed: isAllowed(gate, name) ? gate.allowed : [...gate.allowed, name],
    refused: gate.refused.filter((n) => n !== name),
  };
}

/** A tool's name as the card writes it: the app chose it, so nothing that would reorder or hide the text around it. */
export const shownToolName = (name: string) => name.replace(/[\p{Cc}\p{Cf}]/gu, '').slice(0, 60);
