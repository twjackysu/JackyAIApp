import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';

import { DetailsTab } from './DetailsTab';

import type { DetailsExportResult } from '../../apis/dataAnalysisApis/exportCsv';

const noop = () => {};

function render(
  overrides: {
    exporting?: boolean;
    exportResult?: DetailsExportResult | null;
    exportError?: string | null;
  } = {},
) {
  return renderToStaticMarkup(
    <DetailsTab
      fields={[]}
      detailsFilters={[]}
      onFiltersChange={noop}
      detailsLoading={false}
      detailsError={null}
      detailsRows={[]}
      detailsHeaders={[]}
      detailsTotalCount={0}
      page={0}
      onPageChange={noop}
      onExport={noop}
      exporting={overrides.exporting ?? false}
      exportResult={overrides.exportResult ?? null}
      exportError={overrides.exportError ?? null}
    />,
  );
}

describe('DetailsTab export', () => {
  it('offers the CSV export', () => {
    const html = render();
    expect(html).toContain('匯出 CSV');
    expect(html).not.toContain('disabled');
  });

  it('disables the button while exporting', () => {
    const html = render({ exporting: true });
    expect(html).toContain('匯出中…');
    expect(html).toContain('disabled');
  });

  it('confirms how many rows a complete export holds', () => {
    const html = render({ exportResult: { exported: 2910, total: 2910, filename: 'a.csv' } });
    expect(html).toContain('已匯出 2,910 筆');
    expect(html).not.toContain('單次匯出上限');
  });

  it('warns when the export was cut off at the cap', () => {
    const html = render({ exportResult: { exported: 100000, total: 123456, filename: 'a.csv' } });
    expect(html).toContain('已匯出前 100,000 筆');
    expect(html).toContain('123,456');
    expect(html).toContain('單次匯出上限');
  });

  it('shows why an export failed', () => {
    expect(render({ exportError: 'Another export is running.' })).toContain(
      '匯出失敗：Another export is running.',
    );
  });
});
