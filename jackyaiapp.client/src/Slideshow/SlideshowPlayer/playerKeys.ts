export type PlayerKeyAction =
  | 'togglePlay'
  | 'next'
  | 'previous'
  | 'first'
  | 'last'
  | 'fullscreen'
  | 'exit'
  | 'slower'
  | 'faster';

const ACTIONS: Record<string, PlayerKeyAction> = {
  ' ': 'togglePlay',
  ArrowRight: 'next',
  PageDown: 'next',
  ArrowLeft: 'previous',
  PageUp: 'previous',
  Home: 'first',
  End: 'last',
  f: 'fullscreen',
  F: 'fullscreen',
  Escape: 'exit',
  '+': 'slower',
  '=': 'slower',
  '-': 'faster',
};

/** What a key does in the player, if anything. */
export const playerKeyAction = (key: string): PlayerKeyAction | null => ACTIONS[key] ?? null;

/** The seconds a photo stays: 1 to 30. */
export const clampInterval = (seconds: number) => Math.min(30, Math.max(1, seconds));
