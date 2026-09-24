import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';

import { ImportCsvDialog } from './ImportCsvDialog';

const noop = () => {};

describe('ImportCsvDialog', () => {
  it('renders a trigger button before the dialog is opened', () => {
    const html = renderToStaticMarkup(<ImportCsvDialog onImport={noop} />);
    expect(html).toContain('匯入 CSV');
    // Closed dialogs render nothing visible from MUI's Dialog (Modal unmounts its children).
    expect(html).not.toContain('選擇符合匯入範本欄位');
  });
});
