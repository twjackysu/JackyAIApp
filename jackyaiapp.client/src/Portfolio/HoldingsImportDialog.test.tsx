import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';

import { HoldingsImportDialog } from './HoldingsImportDialog';

describe('HoldingsImportDialog', () => {
  it('renders the import and template-download trigger buttons, dialog closed by default', () => {
    const html = renderToStaticMarkup(<HoldingsImportDialog onImport={vi.fn()} />);

    expect(html).toContain('匯入 CSV');
    expect(html).toContain('下載匯入範本');
    // The sub-dialog's preview table only exists once a file has been picked.
    expect(html).not.toContain('可匯入');
  });
});
