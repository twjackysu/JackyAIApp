/**
 * What each net worth series is called and coloured. A key of a closed backend enum has a fixed
 * label and colour; a free-form one (an AI sub-category, a holding) keeps its key as its label and
 * gets a colour from its name, the same every time.
 */
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
// darkMode: true — see charts/echarts.ts), but stock Tableau 20 was designed for a white
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

/** The series' name: its Chinese label, or the key itself. */
export const seriesLabel = (key: string) => KEY_LABELS[key] ?? key;

/** The series' colour: its fixed one, or one from its name. */
export const seriesColor = (key: string) => KEY_COLORS[key] ?? colorForKey(key);
