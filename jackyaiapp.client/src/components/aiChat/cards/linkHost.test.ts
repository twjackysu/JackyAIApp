import { describe, expect, it } from 'vitest';

import { linkHostOf } from './linkHost';

describe('linkHostOf', () => {
  it('is the host of an https link, with its port', () => {
    expect(linkHostOf('https://example.com/a?b=1')).toBe('example.com');
    expect(linkHostOf('https://example.com:8443/')).toBe('example.com:8443');
  });

  it('shows a lookalike name as punycode, not as the name it imitates', () => {
    // A Cyrillic "а" in place of the Latin one.
    expect(linkHostOf('https://\u0430pple.com/')).toBe('xn--pple-43d.com');
  });

  it('is null for a page on this site or an address it cannot read', () => {
    expect(linkHostOf('/english/repository')).toBeNull();
    expect(linkHostOf('not a url')).toBeNull();
  });
});
