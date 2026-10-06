import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import WordLinkList from './WordLinkList';
import { isLinkableWord } from './wordLinks';

describe('isLinkableWord', () => {
  it.each(['happy', 'ice cream', "don't", 'well-known', 'café'])('links %s', (w) => {
    expect(isLinkableWord(w)).toBe(true);
  });
  it.each(['', '  ', 'a/b', 'foo?bar', '123', 'x<y>', '(adj)'])('does not link %j', (w) => {
    expect(isLinkableWord(w)).toBe(false);
  });
});

describe('WordLinkList', () => {
  it('renders real anchors with encoded hrefs and plain text for odd entries', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter>
        <WordLinkList label="synonyms" words={['glad', 'ice cream', 'a/b']} />
      </MemoryRouter>,
    );
    expect(html).toContain('href="/english/dictionary/glad"');
    expect(html).toContain('href="/english/dictionary/ice%20cream"');
    expect(html).not.toContain('a%2Fb');
    expect(html).toContain('a/b');
    expect(html).toContain('synonyms:');
  });
});
