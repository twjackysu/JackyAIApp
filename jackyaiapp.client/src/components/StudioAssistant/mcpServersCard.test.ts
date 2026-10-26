import { describe, expect, it } from 'vitest';

import { parseMcpServersCard } from './mcpServersCard';

const card = (data: Record<string, unknown>) => ({ kind: 'mcp-servers', title: null, data });

describe('parseMcpServersCard', () => {
  it('keeps the servers as plain text within limits', () => {
    const parsed = parseMcpServersCard(
      card({
        search: ' weather ',
        more: true,
        servers: [
          {
            name: 'io.example/weather',
            title: 'Weather',
            description: 'x'.repeat(500),
            host: 'weather.example.com',
            needs: '需要 token',
            inCatalog: 'yes',
          },
        ],
      }),
    );
    expect(parsed).toEqual({
      search: 'weather',
      more: true,
      servers: [
        {
          name: 'io.example/weather',
          title: 'Weather',
          description: 'x'.repeat(200),
          host: 'weather.example.com',
          url: null,
          needs: '需要 token',
          inCatalog: false,
        },
      ],
    });
  });

  it('names a server by its registry name when it has no title, and drops one without a name', () => {
    const parsed = parseMcpServersCard(
      card({ search: 'a', servers: [{ name: 'io.example/a' }, { title: 'No name' }, 'junk'] }),
    );
    expect(parsed?.servers.map((s) => s.title)).toEqual(['io.example/a']);
  });

  it('draws nothing without a search or any server', () => {
    expect(parseMcpServersCard(card({ servers: [{ name: 'a' }] }))).toBeNull();
    expect(parseMcpServersCard(card({ search: 'a', servers: [] }))).toBeNull();
    expect(parseMcpServersCard(card({ search: 'a', servers: 'x' }))).toBeNull();
  });

  it('keeps at most ten', () => {
    const servers = Array.from({ length: 15 }, (_, i) => ({ name: `s${i}` }));
    expect(parseMcpServersCard(card({ search: 's', servers }))?.servers).toHaveLength(10);
  });

  it('keeps only an https address, to tell an added server (D-127)', () => {
    const urls = (url: unknown) =>
      parseMcpServersCard(card({ search: 'w', servers: [{ name: 'a/b', url }] }))!.servers[0].url;
    expect(urls('https://mcp.example.com/mcp')).toBe('https://mcp.example.com/mcp');
    expect(urls('javascript:alert(1)')).toBeNull();
    expect(urls('http://mcp.example.com/mcp')).toBeNull();
    expect(urls(42)).toBeNull();
  });
});
