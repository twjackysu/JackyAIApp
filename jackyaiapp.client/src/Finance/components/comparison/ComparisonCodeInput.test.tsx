import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';

import { ComparisonCodeInput } from './ComparisonCodeInput';

const noop = () => {};

describe('ComparisonCodeInput', () => {
  it('renders a chip per added code and an enabled compare button', () => {
    const html = renderToStaticMarkup(
      <ComparisonCodeInput
        codes={['2330', 'AAPL']}
        inputValue=""
        onInputChange={noop}
        onAdd={noop}
        onRemove={noop}
        onCompare={noop}
        isComparing={false}
      />,
    );

    expect(html).toContain('2330');
    expect(html).toContain('AAPL');
    expect(html).toContain('比較');
  });

  it('switches to the cap message and disables further input at the 3-code limit', () => {
    const html = renderToStaticMarkup(
      <ComparisonCodeInput
        codes={['2330', 'AAPL', '0050']}
        inputValue=""
        onInputChange={noop}
        onAdd={noop}
        onRemove={noop}
        onCompare={noop}
        isComparing={false}
      />,
    );

    expect(html).toContain('最多比較 3 檔股票');
  });

  it('shows a spinner instead of the label while comparing', () => {
    const html = renderToStaticMarkup(
      <ComparisonCodeInput
        codes={['2330']}
        inputValue=""
        onInputChange={noop}
        onAdd={noop}
        onRemove={noop}
        onCompare={noop}
        isComparing
      />,
    );

    expect(html).not.toContain('>比較<');
  });
});
