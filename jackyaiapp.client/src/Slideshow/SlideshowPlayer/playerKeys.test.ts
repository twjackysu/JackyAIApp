import { describe, expect, it } from 'vitest';

import { clampInterval, playerKeyAction } from './playerKeys';

describe('playerKeyAction', () => {
  it('maps each key the player answers to', () => {
    expect(
      [' ', 'ArrowRight', 'PageDown', 'ArrowLeft', 'PageUp', 'Home', 'End', 'f', 'F', 'Escape'].map(
        playerKeyAction,
      ),
    ).toEqual([
      'togglePlay',
      'next',
      'next',
      'previous',
      'previous',
      'first',
      'last',
      'fullscreen',
      'fullscreen',
      'exit',
    ]);
    expect(['+', '=', '-'].map(playerKeyAction)).toEqual(['slower', 'slower', 'faster']);
  });

  it('any other key does nothing', () => {
    expect(playerKeyAction('a')).toBeNull();
    expect(playerKeyAction('Enter')).toBeNull();
  });
});

describe('clampInterval', () => {
  it('keeps the interval within 1 to 30 seconds', () => {
    expect([0, 1, 12, 30, 31].map(clampInterval)).toEqual([1, 1, 12, 30, 30]);
  });
});
