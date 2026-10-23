import { createTheme, ThemeProvider } from '@mui/material/styles';
import { configureStore } from '@reduxjs/toolkit';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { Provider } from 'react-redux';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { PositionsTable } from './PositionsTable';
import { MASK, PrivacyContext, PrivacyMode } from './privacy';

import type { AssetType, PositionDto } from '@/apis/portfolioApis/types';

import { portfolioApis } from '@/apis/portfolioApis';

const mocks = vi.hoisted(() => ({
  refresh: vi.fn(),
  refreshState: { isLoading: false, originalArgs: undefined as { symbol: string } | undefined },
}));

vi.mock('@/apis/portfolioApis', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/apis/portfolioApis')>()),
  useRefreshPositionPriceMutation: () => [mocks.refresh, mocks.refreshState],
}));

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
  vi.clearAllMocks();
  mocks.refreshState = { isLoading: false, originalArgs: undefined };
  localStorage.clear();
  delete (window as { matchMedia?: unknown }).matchMedia;
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

/** A holding with every field the table reads, for the row and card tests. */
function holding(overrides: Partial<PositionDto> = {}): PositionDto {
  return {
    ...position('2330', 'STOCK', 600_000),
    name: '台積電',
    quantity: 1000,
    currentPrice: 600,
    ...overrides,
  };
}

const privacy = (mode: PrivacyMode) => ({
  mode,
  cycle: () => {},
  value: (s: string) => (mode === 'full' ? s : MASK),
  change: (s: string) => (mode === 'hidden' ? MASK : s),
  showRatio: mode !== 'changes',
});

async function renderWith(
  list: PositionDto[],
  {
    mode = 'full',
    onStockClick,
  }: { mode?: PrivacyMode; onStockClick?: (p: PositionDto) => void } = {},
) {
  const store = configureStore({
    reducer: { [portfolioApis.reducerPath]: portfolioApis.reducer },
    middleware: (gdm) => gdm().concat(portfolioApis.middleware),
  });
  await act(async () =>
    root.render(
      <Provider store={store}>
        <ThemeProvider theme={createTheme()}>
          <PrivacyContext.Provider value={privacy(mode)}>
            <PositionsTable
              positions={list}
              liquidGrossAssetsTWD={1_000_000}
              onStockClick={onStockClick}
            />
          </PrivacyContext.Provider>
        </ThemeProvider>
      </Provider>,
    ),
  );
}

/** Phone width: MUI's `down('sm')` query matches. */
function onPhone() {
  window.matchMedia = ((query: string) => ({
    matches: true,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  })) as unknown as typeof window.matchMedia;
}

const text = () => container.textContent ?? '';
const rows = () => [...container.querySelectorAll('tbody tr')] as HTMLElement[];
const cells = (row: Element) => [...row.querySelectorAll('td')].map((td) => td.textContent);
const chipLabels = (el: Element) =>
  [...el.querySelectorAll('.MuiChip-label')].map((c) => c.textContent);
const cards = () =>
  [...container.querySelectorAll('.MuiAccordionDetails-root > div > div')] as HTMLElement[];
const click = async (el: Element | null | undefined) => {
  if (!el) throw new Error('nothing to click');
  await act(async () => (el as HTMLElement).click());
};

describe('PositionsTable 分組', () => {
  it('沒有持倉：提示建立持倉', async () => {
    await renderWith([]);
    expect(text()).toBe('尚無持倉，點「建立持倉」開始');
  });

  it('依台股、美股、基金、現金、不動產、負債、其他排列，標題帶筆數、占比與合計', async () => {
    await renderWith([
      holding({ assetType: 'LIABILITY', symbol: 'LOAN', marketValueTWD: -500_000 }),
      holding({ symbol: 'VOO', market: 'US', marketValueTWD: 200_000 }),
      holding({ assetType: 'OTHER' as AssetType, symbol: 'X' }),
      holding({ marketValueTWD: 100_000 }),
      holding({ symbol: '0050', marketValueTWD: 50_000 }),
      holding({ assetType: 'REAL_ESTATE', symbol: 'HOUSE', marketValueTWD: 3_000_000 }),
      holding({ assetType: 'CASH', symbol: 'TWD', marketValueTWD: 10_000 }),
      holding({ assetType: 'FUND', symbol: 'F1', marketValueTWD: 20_000 }),
    ]);
    const headers = [...container.querySelectorAll('.MuiAccordionSummary-content')].map(
      (el) => el.textContent,
    );
    expect(headers).toEqual([
      '台股（2）15.0%150,000',
      '美股（1）20.0%200,000',
      '基金（1）2.0%20,000',
      '現金（1）1.0%10,000',
      '不動產（1）3,000,000',
      '負債（1）-50.0%-500,000',
      '其他（1）60.0%600,000',
    ]);
  });

  it('負債的合計用虧損色，其他類別不上色', async () => {
    await renderWith([
      holding({ assetType: 'LIABILITY', symbol: 'LOAN', marketValueTWD: -500_000 }),
      holding(),
    ]);
    const total = (label: string) =>
      [...container.querySelectorAll('.MuiAccordionSummary-content')]
        .find((el) => el.textContent?.startsWith(label))!
        .querySelector('.MuiTypography-body2') as HTMLElement;
    expect(getComputedStyle(total('負債')).color).toBe('rgb(239, 68, 68)');
    expect(getComputedStyle(total('台股')).color).not.toBe('rgb(239, 68, 68)');
  });

  it('隱藏金額遮住合計；只看損益不顯示占比', async () => {
    await renderWith([holding()], { mode: 'hidden' });
    expect(container.querySelector('.MuiAccordionSummary-content')?.textContent).toBe(
      `台股（1）60.0%${MASK}`,
    );
    await renderWith([holding()], { mode: 'changes' });
    expect(container.querySelector('.MuiAccordionSummary-content')?.textContent).toBe(
      `台股（1）${MASK}`,
    );
  });

  it('每組預設展開，各自收合', async () => {
    await renderWith([holding(), holding({ assetType: 'CASH', symbol: 'TWD' })]);
    const summaries = () =>
      [...container.querySelectorAll('.MuiAccordionSummary-root')] as HTMLElement[];
    expect(summaries().map((s) => s.getAttribute('aria-expanded'))).toEqual(['true', 'true']);
    await click(summaries()[0]);
    expect(summaries().map((s) => s.getAttribute('aria-expanded'))).toEqual(['false', 'true']);
    await click(summaries()[0]);
    expect(summaries()[0].getAttribute('aria-expanded')).toBe('true');
  });
});

describe('PositionsTable 桌機表格', () => {
  it('項目：代號與名稱；沒有代號時是名稱與估價方式；標籤依類型、市場、曝險、主題', async () => {
    await renderWith([
      holding({ exposureFactor: 2, tags: ['AI', '半導體'] }),
      holding({ symbol: 'VOO', market: 'US', name: 'Vanguard' }),
      holding({ symbol: 'X', market: '' }),
      holding({ symbol: 'SH', exposureFactor: -1 }),
      holding({ symbol: 'BOND', exposureFactor: 0 }),
      holding({ assetType: 'FUND', symbol: 'F1', name: '基金一號' }),
      holding({ assetType: 'CASH', symbol: '', name: '活存', exposureFactor: 0 }),
      holding({
        assetType: 'REAL_ESTATE',
        symbol: '',
        name: '自住',
        valuationMethod: '實價登錄',
        exposureFactor: 0,
      }),
      holding({ assetType: 'LIABILITY', symbol: '', name: '房貸', exposureFactor: 2 }),
      holding({ assetType: 'WEIRD' as AssetType, symbol: 'W' }),
      holding({
        assetType: 'REAL_ESTATE',
        symbol: 'HOUSE2',
        name: '店面',
        valuationMethod: '估價',
        exposureFactor: 0,
      }),
    ]);
    const item = (lead: string) =>
      rows()
        .map((r) => r.querySelector('td')!)
        .find((td) => td.querySelector('p')?.textContent === lead)!;
    expect(item('2330').textContent).toBe('2330台積電TW曝險×2AI半導體');
    expect(item('VOO').textContent).toBe('VOOVanguardUS');
    expect(chipLabels(item('X'))).toEqual(['股票']);
    expect(chipLabels(item('SH'))).toEqual(['TW', '曝險×-1']);
    expect(chipLabels(item('BOND'))).toEqual(['TW', '0曝險']);
    expect(item('F1').textContent).toBe('F1基金一號基金');
    expect(item('活存').textContent).toBe('活存現金');
    expect(item('自住').textContent).toBe('自住實價登錄不動產');
    // Manually valued rows never carry an exposure chip.
    expect(item('房貸').textContent).toBe('房貸負債');
    expect(chipLabels(item('W'))).toEqual(['股票']);
    expect(item('HOUSE2').textContent).toBe('HOUSE2店面不動產');

    const colour = (el: Element, label: string) =>
      [...el.querySelectorAll('.MuiChip-root')]
        .find((c) => c.textContent === label)!
        .className.match(/MuiChip-color(\w+)/)?.[1];
    expect(colour(item('2330'), 'TW')).toBe('Primary');
    expect(colour(item('2330'), '曝險×2')).toBe('Warning');
    expect(colour(item('BOND'), '0曝險')).toBe('Default');
    expect(colour(item('F1'), '基金')).toBe('Info');
    expect(colour(item('活存'), '現金')).toBe('Success');
    expect(colour(item('自住'), '不動產')).toBe('Warning');
    expect(colour(item('房貸'), '負債')).toBe('Error');
  });

  it('數量、現價、市值、占比；手動估值的類別沒有數量與現價；美元另列原幣市值', async () => {
    await renderWith([
      holding({ quantity: 1234.5, currentPrice: 600 }),
      holding({
        symbol: 'VOO',
        market: 'US',
        currency: 'USD',
        quantity: 10,
        currentPrice: 500.123,
        marketValueLocal: 5_001.23,
        marketValueTWD: 160_000,
      }),
      holding({ assetType: 'CASH', symbol: 'TWD', marketValueTWD: 10_000 }),
    ]);
    expect(cells(rows()[0]).slice(1, 5)).toEqual(['1,234.5', 'TWD 600.00', '600,000', '60.0%']);
    expect(cells(rows()[1]).slice(1, 5)).toEqual(['10', 'USD 500.12', '160,000USD 5,001', '16.0%']);
    expect(cells(rows()[2]).slice(1, 5)).toEqual(['—', '—', '10,000', '1.0%']);
  });

  it('未實現損益：金額與報酬率上色；沒有成本是「—」；股息與估計賣出費用另列', async () => {
    await renderWith([
      holding({ unrealizedPnlTWD: 12_345, unrealizedPnlPct: 2.5, dividendsTWD: 800 }),
      holding({
        symbol: '0050',
        unrealizedPnlTWD: -100,
        unrealizedPnlPct: -1.25,
        estimatedSellCostTWD: 50,
      }),
      holding({ symbol: '2317', unrealizedPnlTWD: 100, unrealizedPnlPct: null }),
    ]);
    expect(cells(rows()[0])[5]).toBe('+12,345+2.50%+息 800');
    expect(cells(rows()[1])[5]).toBe('-100-1.25%-估費 50');
    expect(cells(rows()[2])[5]).toBe('—');
    const colourOf = (row: number, nth: number) =>
      getComputedStyle(
        rows()[row].querySelectorAll('td')[5].querySelectorAll('p, span')[nth] as HTMLElement,
      ).color;
    expect(colourOf(0, 0)).toBe('rgb(34, 197, 94)');
    expect(colourOf(0, 1)).toBe('rgb(34, 197, 94)');
    expect(colourOf(1, 0)).toBe('rgb(239, 68, 68)');
    expect(colourOf(1, 1)).toBe('rgb(239, 68, 68)');
  });

  it('負債的市值用虧損色', async () => {
    await renderWith([holding({ assetType: 'LIABILITY', symbol: 'LOAN', marketValueTWD: -500 })]);
    const value = rows()[0].querySelectorAll('td')[3].querySelector('p, span') as HTMLElement;
    expect(getComputedStyle(value).color).toBe('rgb(239, 68, 68)');
  });

  it('隱藏金額：數量、現價、市值、損益、股息、費用都遮；只看損益：占比與報酬率不顯示', async () => {
    const row = holding({
      unrealizedPnlTWD: 12_345,
      unrealizedPnlPct: 2.5,
      dividendsTWD: 800,
      estimatedSellCostTWD: 50,
    });
    await renderWith([row], { mode: 'hidden' });
    expect(cells(rows()[0]).slice(1)).toEqual([
      MASK,
      `TWD ${MASK}`,
      MASK,
      '60.0%',
      `${MASK}+2.50%+息 ${MASK}-估費 ${MASK}`,
    ]);
    await renderWith([row], { mode: 'changes' });
    expect(cells(rows()[0]).slice(1)).toEqual([
      MASK,
      `TWD ${MASK}`,
      MASK,
      '—',
      `+12,345+息 ${MASK}-估費 ${MASK}`,
    ]);
  });

  it('點股票或有代號的基金開明細；現金、沒代號的基金、沒有處理函式時不動', async () => {
    const onStockClick = vi.fn();
    const list = [
      holding(),
      holding({ assetType: 'FUND', symbol: 'F1' }),
      holding({ assetType: 'FUND', symbol: '', name: '沒代號' }),
      holding({ assetType: 'CASH', symbol: 'BANK' }),
    ];
    await renderWith(list, { onStockClick });
    const byText = (t: string) => rows().find((r) => r.textContent?.includes(t));
    for (const t of ['2330', 'F1', '沒代號', 'BANK']) await click(byText(t));
    expect(onStockClick.mock.calls.map((c) => c[0].symbol)).toEqual(['2330', 'F1']);
    expect(getComputedStyle(byText('2330')!).cursor).toBe('pointer');
    expect(getComputedStyle(byText('BANK')!).cursor).toBe('default');

    await renderWith(list);
    expect(getComputedStyle(byText('2330')!).cursor).toBe('default');
  });

  it('報價失敗：標示並可重抓（帶手續費折數），不會順便開明細；重抓中只有那一檔轉圈停用', async () => {
    localStorage.setItem('portfolio.feeDiscountRate', '0.5');
    const onStockClick = vi.fn();
    const list = [
      holding({ priceFetchFailed: true }),
      holding({ symbol: '0050', priceFetchFailed: true }),
    ];
    await renderWith(list, { onStockClick });
    expect(chipLabels(rows()[0].querySelector('td')!)).toContain('報價失敗');
    const refreshOf = (i: number) =>
      [...rows()[i].querySelectorAll('button')].find(
        (b) => b.querySelector('[data-testid="RefreshIcon"], .MuiCircularProgress-root') != null,
      ) as HTMLButtonElement;
    await click(refreshOf(0));
    expect(mocks.refresh).toHaveBeenCalledWith({ symbol: '2330', feeDiscountRate: 0.5 });
    expect(onStockClick).not.toHaveBeenCalled();

    mocks.refreshState = { isLoading: true, originalArgs: { symbol: '2330' } };
    await renderWith(list, { onStockClick });
    expect(rows()[0].querySelector('.MuiCircularProgress-root')).not.toBeNull();
    expect(refreshOf(0).disabled).toBe(true);
    expect(rows()[1].querySelector('[data-testid="RefreshIcon"]')).not.toBeNull();
    expect(refreshOf(1).disabled).toBe(false);
  });
});

describe('PositionsTable 手機卡片', () => {
  beforeEach(onPhone);

  it('不用表格，改成卡片：代號、標籤、名稱與數量、估價方式、主題、市值、損益', async () => {
    await renderWith([
      holding({
        exposureFactor: 2,
        tags: ['AI'],
        unrealizedPnlTWD: 1_000,
        unrealizedPnlPct: 2.5,
      }),
      holding({
        symbol: 'VOO',
        market: 'US',
        name: 'Vanguard',
        currency: 'USD',
        quantity: 10,
        marketValueLocal: 5_001,
        marketValueTWD: 160_000,
      }),
      holding({
        assetType: 'REAL_ESTATE',
        symbol: '',
        name: '自住',
        valuationMethod: '實價登錄',
        marketValueTWD: 3_000_000,
        exposureFactor: 0,
      }),
      holding({ assetType: 'FUND', symbol: '', name: '沒代號', quantity: 12.5 }),
      holding({ assetType: 'LIABILITY', symbol: '', name: '房貸', marketValueTWD: -500 }),
      holding({
        assetType: 'CASH',
        symbol: '',
        name: '活存',
        valuationMethod: '手填',
        marketValueTWD: 10_000,
        exposureFactor: 0,
      }),
      holding({ assetType: 'WEIRD' as AssetType, symbol: 'W', unrealizedPnlTWD: 5 }),
    ]);
    expect(container.querySelector('table')).toBeNull();
    expect(cards().map((el) => el.textContent)).toEqual([
      '2330TW曝險×2台積電 · 1,000AI600,000+1,000 (+2.50%)',
      'VOOUSVanguard · 10160,000USD 5,001',
      '沒代號基金12.5600,000',
      '活存現金10,000',
      '自住不動產實價登錄3,000,000',
      '房貸負債-500',
      'W股票台積電 · 1,000600,000',
    ]);
    const liability = cards().find((c) => c.textContent?.startsWith('房貸'))!;
    const value = [...liability.querySelectorAll('p, span')].find(
      (el) => el.textContent === '-500',
    ) as HTMLElement;
    expect(getComputedStyle(value).color).toBe('rgb(239, 68, 68)');
  });

  it('報價失敗：卡片右側顯示「報價失敗」與重抓，取代市值', async () => {
    await renderWith([holding({ priceFetchFailed: true })]);
    expect(cards()[0].textContent).toBe('2330TW台積電 · 1,000報價失敗');
    await click(cards()[0].querySelector('[data-testid="RefreshIcon"]')!.closest('button'));
    expect(mocks.refresh).toHaveBeenCalledWith({ symbol: '2330', feeDiscountRate: 0.28 });

    mocks.refreshState = { isLoading: true, originalArgs: { symbol: '2330' } };
    await renderWith([
      holding({ priceFetchFailed: true }),
      holding({ symbol: '0050', priceFetchFailed: true }),
    ]);
    expect(cards()[0].querySelector('.MuiCircularProgress-root')).not.toBeNull();
    expect(cards()[1].querySelector('.MuiCircularProgress-root')).toBeNull();
    expect(cards()[1].querySelector('[data-testid="RefreshIcon"]')).not.toBeNull();
  });

  it('隱私：隱藏金額遮數量、市值與損益金額；只看損益不顯示報酬率', async () => {
    const row = holding({ unrealizedPnlTWD: 1_000, unrealizedPnlPct: 2.5 });
    await renderWith([row], { mode: 'hidden' });
    expect(cards()[0].textContent).toBe(`2330TW台積電 · ${MASK}${MASK}${MASK} (+2.50%)`);
    await renderWith([row], { mode: 'changes' });
    expect(cards()[0].textContent).toBe(`2330TW台積電 · ${MASK}${MASK}+1,000`);
  });

  it('點卡片：股票開明細，現金不動', async () => {
    const onStockClick = vi.fn();
    await renderWith([holding(), holding({ assetType: 'CASH', symbol: 'TWD' })], {
      onStockClick,
    });
    await click(cards()[0]);
    await click(cards()[1]);
    expect(onStockClick.mock.calls.map((c) => c[0].symbol)).toEqual(['2330']);
  });
});
