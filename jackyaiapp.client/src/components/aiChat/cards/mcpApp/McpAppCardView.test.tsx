import { renderToStaticMarkup } from 'react-dom/server';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import McpAppCardView from './McpAppCardView';

const mocks = vi.hoisted(() => ({ app: vi.fn() }));
vi.mock('@/apis/connectorsApis/connectorsApis', () => ({
  useGetMcpAppQuery: mocks.app,
  useCallFromMcpAppMutation: () => [vi.fn()],
}));

const card = {
  kind: 'mcp-app',
  title: null,
  data: {
    installationId: 'i1',
    connector: '天氣',
    tool: 'forecast',
    title: '天氣預報',
    resourceUri: 'ui://weather/forecast.html',
    input: { city: '台北' },
    result: { content: [{ type: 'text', text: '台北明天晴，28 度' }] },
  },
};

const render = () => renderToStaticMarkup(<McpAppCardView card={card} />);

describe('McpAppCardView', () => {
  beforeEach(() => mocks.app.mockReset());

  it('draws the app in a scripts-only sandbox under a line naming whose it is', () => {
    mocks.app.mockReturnValue({
      data: {
        html: '<html><body><script>1</script></body></html>',
        connectDomains: ['https://api.weather.example.com'],
        resourceDomains: [],
        prefersBorder: null,
        connector: '天氣',
        host: 'mcp.weather.example.com',
      },
      isLoading: false,
    });

    const html = render();

    expect(html).toContain('sandbox="allow-scripts"');
    expect(html).not.toContain('allow-same-origin');
    expect(html).toContain('Content-Security-Policy');
    expect(html).toContain('connect-src https://api.weather.example.com');
    expect(html).toContain('來自 mcp.weather.example.com');
    expect(html).toContain('「天氣」的介面');
    expect(mocks.app).toHaveBeenCalledWith({
      installationId: 'i1',
      uri: 'ui://weather/forecast.html',
    });
  });

  it('shows the result as text when the app cannot be read', () => {
    mocks.app.mockReturnValue({
      error: { status: 502, data: { error: { message: '連不上' } } },
      isLoading: false,
    });

    const html = render();

    expect(html).not.toContain('<iframe');
    expect(html).toContain('介面沒有載入');
    expect(html).toContain('台北明天晴，28 度');
  });

  it('draws nothing for a card it cannot read', () => {
    mocks.app.mockReturnValue({ isLoading: true });

    expect(
      renderToStaticMarkup(
        <McpAppCardView card={{ ...card, data: { ...card.data, resourceUri: 'https://x' } }} />,
      ),
    ).toBe('');
  });
});
