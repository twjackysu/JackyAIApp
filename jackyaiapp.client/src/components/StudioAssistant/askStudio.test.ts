import { afterEach, describe, expect, it, vi } from 'vitest';

import { ASK_STUDIO_EVENT, askStudio, askedText } from './askStudio';

describe('askStudio', () => {
  const heard = vi.fn((e: Event) => askedText(e));
  afterEach(() => {
    window.removeEventListener(ASK_STUDIO_EVENT, heard);
    heard.mockClear();
  });

  it('tells Studio AI the message, trimmed', () => {
    window.addEventListener(ASK_STUDIO_EVENT, heard);
    askStudio('  給我今天的簡報 ');
    expect(heard).toHaveReturnedWith('給我今天的簡報');
  });

  it('asks nothing for an empty message', () => {
    window.addEventListener(ASK_STUDIO_EVENT, heard);
    askStudio('   ');
    expect(heard).not.toHaveBeenCalled();
  });
});

describe('askedText', () => {
  it('reads only a message that is text', () => {
    expect(askedText(new CustomEvent(ASK_STUDIO_EVENT, { detail: { text: 1 } }))).toBeNull();
    expect(askedText(new CustomEvent(ASK_STUDIO_EVENT))).toBeNull();
    expect(
      askedText(new CustomEvent(ASK_STUDIO_EVENT, { detail: { text: 'x'.repeat(3000) } })),
    ).toHaveLength(2000);
  });
});
