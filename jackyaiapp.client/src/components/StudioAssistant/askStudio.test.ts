import { afterEach, describe, expect, it, vi } from 'vitest';

import { ASK_STUDIO_EVENT, askStudio, askedOf } from './askStudio';

describe('askStudio', () => {
  const heard = vi.fn((e: Event) => askedOf(e));
  afterEach(() => {
    window.removeEventListener(ASK_STUDIO_EVENT, heard);
    heard.mockClear();
  });

  it('tells Studio AI the message, trimmed, into the conversation on screen', () => {
    window.addEventListener(ASK_STUDIO_EVENT, heard);
    askStudio('  給我今天的簡報 ');
    expect(heard).toHaveReturnedWith({ text: '給我今天的簡報' });
  });

  it('can ask for a new conversation', () => {
    window.addEventListener(ASK_STUDIO_EVENT, heard);
    askStudio('給我今天的簡報', { fresh: true });
    expect(heard).toHaveReturnedWith({ text: '給我今天的簡報', fresh: true });
  });

  it('asks nothing for an empty message', () => {
    window.addEventListener(ASK_STUDIO_EVENT, heard);
    askStudio('   ');
    expect(heard).not.toHaveBeenCalled();
  });
});

describe('askedOf', () => {
  it('reads only a message that is text', () => {
    expect(askedOf(new CustomEvent(ASK_STUDIO_EVENT, { detail: { text: 1 } }))).toBeNull();
    expect(askedOf(new CustomEvent(ASK_STUDIO_EVENT))).toBeNull();
    expect(
      askedOf(new CustomEvent(ASK_STUDIO_EVENT, { detail: { text: 'x'.repeat(3000) } }))?.text,
    ).toHaveLength(2000);
  });

  it('takes a new conversation only when it is asked for as such', () => {
    expect(
      askedOf(new CustomEvent(ASK_STUDIO_EVENT, { detail: { text: 'x', fresh: 'yes' } })),
    ).toEqual({ text: 'x' });
  });
});
