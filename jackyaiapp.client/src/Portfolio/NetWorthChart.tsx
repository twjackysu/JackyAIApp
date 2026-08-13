import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import ButtonGroup from '@mui/material/ButtonGroup';
import CircularProgress from '@mui/material/CircularProgress';
import LinearProgress from '@mui/material/LinearProgress';
import Typography from '@mui/material/Typography';
import ReactEcharts from 'echarts-for-react';
import { useState } from 'react';

import { MASK, usePrivacy } from './privacy';

import { useGetNetWorthHistoryQuery } from '@/apis/portfolioApis';
import { NetWorthDataPoint, NetWorthGroupBy, NetWorthGroupValue } from '@/apis/portfolioApis/types';
import { ECHARTS_THEME, ECOption } from '@/DataAnalysis/components/summarizeChart/echarts';
import { fmtCompact } from '@/utils/format';

const RANGES: { label: string; days: number }[] = [
  { label: '1M', days: 30 },
  { label: '3M', days: 90 },
  { label: '6M', days: 180 },
];

// "liquidity" isn't a real backend groupBy mode — it's a client-side re-bucketing of the same
// assetType-mode response (TwStock/UsStock/Cash/Fund collapsed into one "動產" series, Property
// kept as "不動產", Liability kept as-is), so selecting it fetches with groupBy=assetType under
// the hood rather than adding a new backend enum member/endpoint param for what's really just a
// different way to look at data we already have.
type ViewBy = NetWorthGroupBy | 'liquidity';

const VIEW_BY_OPTIONS: { label: string; value: ViewBy }[] = [
  { label: '資產類型', value: 'assetType' },
  { label: '動產/不動產', value: 'liquidity' },
  { label: '產業', value: 'category' },
  { label: '子類別', value: 'subCategory' },
  { label: '個股明細', value: 'holding' },
];

const LIQUID_KEY = 'Liquid';

/** Collapse an assetType-mode day's groups into 動產(Liquid)/不動產(Property)/負債(Liability). */
function toLiquidityGroups(groups: NetWorthGroupValue[]): NetWorthGroupValue[] {
  let liquid = 0,
    property = 0,
    liability = 0;
  for (const g of groups) {
    if (g.key === 'Property') property += g.valueTWD;
    else if (g.key === 'Liability') liability += g.valueTWD;
    else liquid += g.valueTWD;
  }
  return [
    { key: LIQUID_KEY, valueTWD: liquid },
    { key: 'Property', valueTWD: property },
    { key: 'Liability', valueTWD: liability },
  ].filter((g) => g.valueTWD !== 0);
}

// One combined map for every key any groupBy mode can produce — assetType and category modes
// both use "Cash"/"Liability", and sharing one entry keeps that color/label consistent across
// modes. A key not listed here falls back to its raw key as the label (fine — assetType/category/
// currency are closed backend enums, so this "shouldn't happen" for them) and a color from
// DYNAMIC_PALETTE (see colorForKey below) — subCategory is deliberately open-ended free-form AI
// output, so most of its series are expected to take this path rather than get a fixed entry here.
const KEY_LABELS: Record<string, string> = {
  // assetType
  TwStock: '台股',
  UsStock: '美股',
  Cash: '現金',
  Fund: '基金',
  // liquidity mode only — everything except Property/Liability, collapsed into one series.
  Liquid: '動產',
  // "Property", not "RealEstate": the latter is PortfolioCategory.RealEstate (REIT stocks/ETFs,
  // category-mode only) — a different concept from the user's own literal real-estate holdings,
  // which use this "Property" key in BOTH assetType and category mode (see PortfolioService.GroupKey).
  Property: '不動產',
  Liability: '負債',
  // category (PortfolioCategory enum names)
  Semiconductor: '半導體',
  TechSoftware: '科技軟體',
  Financials: '金融',
  Manufacturing: '傳產製造',
  Consumer: '民生消費',
  Healthcare: '醫療生技',
  Materials: '能源原物料',
  Utilities: '電信公用',
  RealEstate: '房地產REITs',
  BroadMarket: '市值型',
  DividendEquity: '高股息',
  Bond: '債券',
  Other: '其他',
  // currency (button removed, but the backend groupBy mode still exists — keep the labels)
  TWD: '台幣',
  USD: '美元',
  // subCategory: 現金/負債/未分類 are already the raw key text (no translation needed); every
  // other key is a free-form AI-generated label and passes through as-is via the `?? key` fallback.
};

const KEY_COLORS: Record<string, string> = {
  TwStock: '#3B82F6',
  UsStock: '#10B981',
  Cash: '#A78BFA',
  Fund: '#EC4899',
  Liquid: '#3B82F6',
  Property: '#F59E0B',
  Liability: '#ef4444',
  Semiconductor: '#3B82F6',
  TechSoftware: '#6366F1',
  Financials: '#F59E0B',
  Manufacturing: '#78716C',
  Consumer: '#F97316',
  Healthcare: '#14B8A6',
  Materials: '#84CC16',
  Utilities: '#0EA5E9',
  RealEstate: '#D946EF',
  BroadMarket: '#10B981',
  DividendEquity: '#EAB308',
  Bond: '#8B5CF6',
  Other: '#9CA3AF',
  TWD: '#3B82F6',
  USD: '#10B981',
  // subCategory's fixed keys — reuse the same colors as their assetType-mode counterparts. Every
  // other subCategory key (free-form AI labels) gets a color from DYNAMIC_PALETTE instead — see
  // colorForKey below.
  現金: '#A78BFA',
  負債: '#ef4444',
  不動產: '#F59E0B',
  未分類: '#9CA3AF',
};

// Fallback base palette for keys with no fixed entry in KEY_COLORS — almost entirely subCategory/
// holding's free-form labels, which can't be enumerated ahead of time the way the closed enums
// above can. Tableau's classic 20-color categorical set ("Tableau 20" from Tableau Desktop's
// built-in palette — distinct from matplotlib's tab20) — designed so 20 series stay visually
// tell-apart-able at a glance, far more headroom than a hand-picked 16.
const TABLEAU20_RAW = [
  '#4E79A7',
  '#A0CBE8',
  '#F28E2B',
  '#FFBE7D',
  '#59A14F',
  '#8CD17D',
  '#B6992D',
  '#F1CE63',
  '#499894',
  '#86BCB6',
  '#E15759',
  '#FF9D9A',
  '#79706E',
  '#BAB0AC',
  '#D37295',
  '#FABFD2',
  '#B07AA1',
  '#D4A6C8',
  '#9D7660',
  '#D7B5A6',
];

function hexToHsl(hex: string): [h: number, s: number, l: number] {
  const r = parseInt(hex.slice(1, 3), 16) / 255;
  const g = parseInt(hex.slice(3, 5), 16) / 255;
  const b = parseInt(hex.slice(5, 7), 16) / 255;
  const max = Math.max(r, g, b);
  const min = Math.min(r, g, b);
  const l = (max + min) / 2;
  if (max === min) return [0, 0, l * 100];
  const d = max - min;
  const s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
  let h: number;
  switch (max) {
    case r:
      h = (g - b) / d + (g < b ? 6 : 0);
      break;
    case g:
      h = (b - r) / d + 2;
      break;
    default:
      h = (r - g) / d + 4;
  }
  return [h * 60, s * 100, l * 100];
}

function hslToHex(h: number, s: number, l: number): string {
  const S = s / 100,
    L = l / 100;
  const c = (1 - Math.abs(2 * L - 1)) * S;
  const x = c * (1 - Math.abs(((h / 60) % 2) - 1));
  const m = L - c / 2;
  let [r, g, b] = [0, 0, 0];
  if (h < 60) [r, g, b] = [c, x, 0];
  else if (h < 120) [r, g, b] = [x, c, 0];
  else if (h < 180) [r, g, b] = [0, c, x];
  else if (h < 240) [r, g, b] = [0, x, c];
  else if (h < 300) [r, g, b] = [x, 0, c];
  else [r, g, b] = [c, 0, x];
  const toHex = (v: number) =>
    Math.round((v + m) * 255)
      .toString(16)
      .padStart(2, '0');
  return `#${toHex(r)}${toHex(g)}${toHex(b)}`;
}

// The chart always renders on a dark background (ECHARTS_THEME is 'data-analysis-dark',
// darkMode: true — see summarizeChart/echarts.ts), but stock Tableau 20 was designed for a white
// canvas: several of its entries (A0CBE8, F1CE63, BAB0AC, FABFD2, D4A6C8, D7B5A6, FFBE7D...) are
// pale pastels that wash out and lose distinctiveness against dark grid backgrounds. Re-anchor
// every base color's saturation/lightness into a range that stays vivid and readable on dark,
// keeping each entry's original hue (so the palette is still recognizably "Tableau 20").
const TABLEAU20_DARK = TABLEAU20_RAW.map((hex) => {
  const [h, s] = hexToHsl(hex);
  return hslToHex(h, Math.max(s, 55), 62);
});

/// Extends TABLEAU20_DARK past its 20 entries: cycle 0 is the base (dark-adjusted) color
/// verbatim, each further cycle rotates its hue by a golden-angle-ish step and nudges lightness,
/// so a chart with e.g. 45 active series still gets 45 distinct-looking colors instead of the
/// base 20 repeating exactly.
function paletteColor(index: number): string {
  const base = TABLEAU20_DARK[index % TABLEAU20_DARK.length];
  const cycle = Math.floor(index / TABLEAU20_DARK.length);
  if (cycle === 0) return base;
  const [h, s, l] = hexToHsl(base);
  const hueShift = (cycle * 137) % 360;
  const lightnessShift = ((cycle * 17) % 30) - 15;
  return hslToHex((h + hueShift) % 360, s, Math.min(75, Math.max(40, l + lightnessShift)));
}

/// Deterministic hash → same palette index every time for the same key, so a given label keeps
/// its color across re-renders, date-range switches, and even reloads (not assigned by array
/// index, which would shift whenever the set of active keys for the visible range changes).
/// Hashes into a wide index range (not just TABLEAU20_DARK.length) so paletteColor's recursive
/// cycles are actually reachable instead of every key landing in cycle 0.
function colorForKey(key: string): string {
  let hash = 0;
  for (let i = 0; i < key.length; i++) hash = (hash * 31 + key.charCodeAt(i)) | 0;
  return paletteColor(Math.abs(hash) % (TABLEAU20_DARK.length * 50));
}

// Some groups (e.g. Liability) are negative — fmtCompact already compares by magnitude, so a
// debt over 1M still gets the "-X.XM" treatment instead of a raw number.
const fmt = fmtCompact;

export function NetWorthChart() {
  const [days, setDays] = useState(90);
  const [groupBy, setGroupBy] = useState<ViewBy>('assetType');
  // Series names the user has clicked off in the custom legend below — ECharts' legend component
  // still drives series show/hide via `legend.selected` even with `legend.show: false`, so this
  // stays fully interactive without bringing back the native legend UI (pagination/overlap).
  const [hiddenNames, setHiddenNames] = useState<Set<string>>(new Set());
  const toggleName = (name: string) =>
    setHiddenNames((prev) => {
      const next = new Set(prev);
      if (next.has(name)) next.delete(name);
      else next.add(name);
      return next;
    });
  const { mode } = usePrivacy();
  // liquidity mode rides on the assetType-mode response — see toLiquidityGroups.
  const queryGroupBy: NetWorthGroupBy = groupBy === 'liquidity' ? 'assetType' : groupBy;
  // isFetching (not just isLoading): switching view-by/range to a groupBy+days combo that isn't
  // cached yet fires a real request — isLoading alone only covers the very first ever fetch, so a
  // later switch to a not-yet-cached combo showed no loading affordance at all and just looked
  // broken/stale until the data quietly arrived.
  const {
    data: res,
    isLoading,
    isFetching,
  } = useGetNetWorthHistoryQuery({
    days,
    groupBy: queryGroupBy,
  });
  const rawPoints = res?.data ?? [];
  const points: NetWorthDataPoint[] =
    groupBy === 'liquidity'
      ? rawPoints.map((p) => ({ ...p, groups: toLiquidityGroups(p.groups) }))
      : rawPoints;

  if (isLoading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
        <CircularProgress />
      </Box>
    );
  }

  if (points.length === 0) {
    // Still resolving a not-yet-cached combo (belt-and-suspenders alongside the isLoading check
    // above) — show the spinner instead of prematurely claiming there's no data.
    if (isFetching) {
      return (
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
          <CircularProgress />
        </Box>
      );
    }
    return (
      <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>
        資料不足，更新持倉後即可看到圖表
      </Typography>
    );
  }

  const dates = points.map((p) => p.date);
  const totals = points.map((p) => p.totalTWD);

  // Every distinct group key seen across the whole range — different days can have different
  // active groups (e.g. a category only appears once its symbol gets held/classified).
  const allKeys = Array.from(new Set(points.flatMap((p) => p.groups.map((g) => g.key))));
  const breakdown = allKeys
    .map((key) => ({
      key,
      name: KEY_LABELS[key] ?? key,
      color: KEY_COLORS[key] ?? colorForKey(key),
      values: points.map((p) => p.groups.find((g) => g.key === key)?.valueTWD ?? 0),
    }))
    .filter((s) => s.values.some((v) => v !== 0));

  const option: ECOption = {
    tooltip: {
      trigger: 'axis',
      // Renders the tooltip as a document.body child instead of inside the chart's own container,
      // which otherwise clips it whenever it would overflow the card's edges (MUI Card/Paper clips
      // to its rounded corners via overflow:hidden) — happened on any side, worse with more series/
      // a taller legend squeezing the plot area.
      appendToBody: true,
      formatter: (params: unknown) => {
        const p = params as {
          axisValue: string;
          seriesName: string;
          value: number;
          dataIndex: number;
        }[];
        const date = p[0]?.axisValue ?? '';
        const dayTotal = totals[p[0]?.dataIndex ?? 0] || 0;
        const line = (s: { seriesName: string; value: number }) => {
          if (mode === 'full') return `${s.seriesName}：NT$${fmt(s.value)}`;
          // 'changes' hides both amounts and ratios; 'hidden' shows share-of-total instead.
          if (mode === 'changes') return `${s.seriesName}：${MASK}`;
          const pct = dayTotal > 0 ? (s.value / dayTotal) * 100 : 0;
          return `${s.seriesName}：${pct.toFixed(1)}%`;
        };
        // The stack already sums to the total, so no separate "總資產" series is rendered —
        // just surface the number (or masked value) in the tooltip for a quick read.
        const totalLine =
          mode === 'full'
            ? `<b>總資產：NT$${fmt(dayTotal)}</b>`
            : mode === 'changes'
              ? `<b>總資產：${MASK}</b>`
              : null;
        return [`<b>${date}</b>`, totalLine, ...p.map(line)].filter(Boolean).join('<br/>');
      },
    },
    // ECharts' own legend is hidden (see the custom HTML legend below the chart instead) — a
    // scrolling single-row legend needed pagination clicks to see every series, and a wrapped
    // multi-row one pushed into/over the xAxis labels since a fixed-height grid can't know how
    // many rows the legend will take. A separate DOM element below the chart just flex-wraps
    // naturally with the page, no chart-internal height math needed either way. The legend
    // component still drives series show/hide via `selected` even while hidden, so clicking the
    // custom legend below stays fully interactive — see hiddenNames/toggleName above.
    legend: {
      show: false,
      selected: Object.fromEntries(breakdown.map((s) => [s.name, !hiddenNames.has(s.name)])),
    },
    grid: { top: 16, right: 16, bottom: 24, left: mode === 'full' ? 70 : 24 },
    xAxis: { type: 'category', data: dates, boundaryGap: false },
    yAxis: {
      type: 'value',
      // Mask the scale numbers unless fully visible; stack height/proportions stay accurate.
      axisLabel: { formatter: (v: number) => (mode === 'full' ? `${fmt(v)}` : '') },
    },
    series: breakdown.map((s) => ({
      name: s.name,
      type: 'line' as const,
      // Any group that ever goes negative (Liability under assetType/category grouping — or in
      // principle any group under currency grouping) gets its own stack, not the shared 'total'
      // one: ECharts stacks cumulatively, so a negative series sharing a stack with positive ones
      // renders as "gross assets, then a jump down" (its cumulative value = everything below it
      // + itself) instead of its own value dipping cleanly from 0. Its own stack group renders it
      // independently, from a 0 baseline down to its actual (negative) value.
      stack: s.values.some((v) => v < 0) ? `neg-${s.key}` : 'total',
      data: s.values,
      smooth: true,
      lineStyle: { width: 1.5, color: s.color },
      itemStyle: { color: s.color },
      areaStyle: { color: s.color, opacity: 0.65 },
      symbol: 'none' as const,
    })),
  };

  return (
    <Box>
      <Box
        sx={{
          display: 'flex',
          flexWrap: 'wrap',
          justifyContent: 'space-between',
          alignItems: 'center',
          gap: 1,
          mb: 1,
        }}
      >
        <ButtonGroup size="small" variant="outlined">
          {VIEW_BY_OPTIONS.map((v) => (
            <Button
              key={v.value}
              onClick={() => setGroupBy(v.value)}
              variant={groupBy === v.value ? 'contained' : 'outlined'}
            >
              {v.label}
            </Button>
          ))}
        </ButtonGroup>
        <ButtonGroup size="small" variant="outlined">
          {RANGES.map((r) => (
            <Button
              key={r.days}
              onClick={() => setDays(r.days)}
              variant={days === r.days ? 'contained' : 'outlined'}
            >
              {r.label}
            </Button>
          ))}
        </ButtonGroup>
      </Box>
      {/* Cached data for this exact (days, groupBy) combo is stale and being revalidated in the
          background, OR we already fell through the isLoading gate above but a slower network
          still has the request in flight — either way, some visible feedback beats the chart just
          silently sitting there looking frozen/wrong until it updates. */}
      {isFetching && <LinearProgress sx={{ mb: 1 }} />}
      {/* notMerge: without it, ECharts incrementally patches the previous option onto the new one
          rather than replacing it outright — switching between view-by modes with very different
          series (e.g. subCategory's free-form AI labels vs assetType's fixed set) left stale
          series names lingering in the tooltip from whatever was hovered under the previous mode. */}
      <ReactEcharts option={option} notMerge theme={ECHARTS_THEME} style={{ height: 280 }} />
      <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1.5, mt: 1, justifyContent: 'center' }}>
        {breakdown.map((s) => {
          const hidden = hiddenNames.has(s.name);
          return (
            <Box
              key={s.key}
              onClick={() => toggleName(s.name)}
              sx={{
                display: 'flex',
                alignItems: 'center',
                gap: 0.5,
                cursor: 'pointer',
                userSelect: 'none',
                opacity: hidden ? 0.4 : 1,
              }}
            >
              <Box
                sx={{
                  width: 10,
                  height: 10,
                  borderRadius: '50%',
                  bgcolor: hidden ? 'text.disabled' : s.color,
                }}
              />
              <Typography
                variant="caption"
                color="text.secondary"
                sx={{ textDecoration: hidden ? 'line-through' : 'none' }}
              >
                {s.name}
              </Typography>
            </Box>
          );
        })}
      </Box>
    </Box>
  );
}
