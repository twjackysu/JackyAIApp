import { createTheme, ThemeProvider } from '@mui/material/styles';
import { configureStore } from '@reduxjs/toolkit';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { Provider } from 'react-redux';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { PositionsTable } from './PositionsTable';

import type { AssetType, PositionDto } from '@/apis/portfolioApis/types';

import { portfolioApis } from '@/apis/portfolioApis';

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

function position(
  symbol: string,
  assetType: AssetType,
  marketValueTWD: number,
  market = 'TW',
): PositionDto {
  return {
    assetType,
    symbol,
    name: symbol,
    market,
    currency: 'TWD',
    quantity: 1,
    currentPrice: marketValueTWD,
    marketValueLocal: marketValueTWD,
    marketValueTWD,
    exposureFactor: assetType === 'STOCK' || assetType === 'FUND' ? 1 : 0,
    priceFetchFailed: false,
    dividendsTWD: 0,
    estimatedSellCostTWD: 0,
    tags: [],
  };
}

// Liquid gross assets: 600 + 200 + 100 + 100 = 1,000. The 3,000 house and the 500 of debt are
// listed too, but the house must not be part of the denominator.
const positions: PositionDto[] = [
  position('2330', 'STOCK', 600),
  position('VOO', 'STOCK', 200, 'US'),
  position('FUND1', 'FUND', 100),
  position('TWD', 'CASH', 100),
  position('HOUSE', 'REAL_ESTATE', 3_000),
  position('LOAN', 'LIABILITY', -500),
];

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
});

async function render() {
  const store = configureStore({
    reducer: { [portfolioApis.reducerPath]: portfolioApis.reducer },
    middleware: (gdm) => gdm().concat(portfolioApis.middleware),
  });
  await act(async () =>
    root.render(
      <Provider store={store}>
        <ThemeProvider theme={createTheme()}>
          <PositionsTable positions={positions} liquidGrossAssetsTWD={1_000} />
        </ThemeProvider>
      </Provider>,
    ),
  );
}

/** The 占比 cell of a holding's row (the column after 市值). */
function rowShare(symbol: string): string | undefined {
  const row = [...container.querySelectorAll('tbody tr')].find((tr) =>
    tr.textContent?.includes(symbol),
  );
  return row?.querySelectorAll('td')[4]?.textContent ?? undefined;
}

/** The share shown in a group's header, if any. */
function groupShare(label: string): string | undefined {
  const header = [...container.querySelectorAll('.MuiAccordionSummary-content')].find((el) =>
    el.textContent?.startsWith(label),
  );
  return header?.querySelector('.MuiTypography-caption')?.textContent ?? undefined;
}

describe('PositionsTable 占比', () => {
  it('divides by liquid gross assets, leaving real estate out', async () => {
    await render();

    expect(groupShare('台股')).toBe('60.0%');
    expect(groupShare('美股')).toBe('20.0%');
    expect(groupShare('基金')).toBe('10.0%');
    expect(groupShare('現金')).toBe('10.0%');
    expect(rowShare('2330')).toBe('60.0%');
    expect(rowShare('VOO')).toBe('20.0%');
  });

  it('gives real estate no share', async () => {
    await render();

    expect(groupShare('不動產')).toBeUndefined();
    expect(rowShare('HOUSE')).toBe('—');
  });

  it('shows liability as a negative share of liquid gross assets', async () => {
    await render();

    expect(groupShare('負債')).toBe('-50.0%');
    expect(rowShare('LOAN')).toBe('-50.0%');
  });
});
