import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';

import { SourcePanel } from './SourcePanel';

const noop = () => {};

function render(activeSourceId: string | null) {
  return renderToStaticMarkup(
    <SourcePanel
      isLoggedIn
      sources={[
        {
          id: 'src-1',
          name: 'My Warehouse',
          connectorType: 'MsSql',
          defaultTableName: 'dbo.Sales',
          config: {},
          createdAt: '2026-01-01T00:00:00Z',
        },
      ]}
      activeSourceId={activeSourceId}
      tableNameInput="dbo.Orders"
      sessionInfo={null}
      onSourceChange={noop}
      onTableNameChange={noop}
      onApplyTableName={noop}
      onOpenManager={noop}
      onOpenUpload={noop}
      onClearSession={noop}
    />,
  );
}

describe('SourcePanel', () => {
  it('shows the default source in the 來源 field when it is the active one', () => {
    // MUI Select renders nothing for a value of '', which is what the default source used to be.
    expect(render(null)).toContain('預設 (JackyAI SQL / dbo.Orders)');
  });

  it('shows a saved source by name when it is the active one', () => {
    const html = render('src-1');
    expect(html).toContain('My Warehouse');
    expect(html).not.toContain('預設 (JackyAI SQL / dbo.Orders)');
  });
});
