import { StoryAllocation, StorySection } from './storyAllocation';

// IG story canvas. Everything is drawn client-side on a <canvas> — no server or paid service.
export const STORY_WIDTH = 1080;
export const STORY_HEIGHT = 1920;

const PAD = 64;
// IG overlays its own UI on roughly the top/bottom 220px of a story, so content stays between.
const CONTENT_TOP = 440;
const CONTENT_BOTTOM = 1700;
const SECTION_GAP = 48;
const SECTION_HEIGHT = (CONTENT_BOTTOM - CONTENT_TOP - SECTION_GAP) / 2;

// Latin faces first so digits/tickers get a crisp UI font; CJK falls through per glyph.
const FONT =
  '-apple-system, "SF Pro Display", "Segoe UI", Roboto, "PingFang TC", "Microsoft JhengHei", "Noto Sans TC", sans-serif';

const TEXT = '#F5F7FB';
const MUTED = 'rgba(226, 232, 240, 0.55)';
const OTHER_COLOR = '#6B7280';

const THEME = {
  TW: {
    label: '台股',
    sub: 'TAIWAN',
    accent: '#FF7A6B',
    palette: [
      '#FF6B6B',
      '#FF9F43',
      '#FECA57',
      '#FF6B9D',
      '#F368E0',
      '#FFB38A',
      '#E17055',
      '#C56CF0',
    ],
  },
  US: {
    label: '美股',
    sub: 'UNITED STATES',
    accent: '#54A0FF',
    palette: [
      '#4FD1C5',
      '#48DBFB',
      '#54A0FF',
      '#7D7BFF',
      '#A29BFE',
      '#00D2D3',
      '#82CCDD',
      '#6C9BFF',
    ],
  },
} as const;

function roundRect(
  ctx: CanvasRenderingContext2D,
  x: number,
  y: number,
  w: number,
  h: number,
  r: number,
) {
  ctx.beginPath();
  ctx.moveTo(x + r, y);
  ctx.arcTo(x + w, y, x + w, y + h, r);
  ctx.arcTo(x + w, y + h, x, y + h, r);
  ctx.arcTo(x, y + h, x, y, r);
  ctx.arcTo(x, y, x + w, y, r);
  ctx.closePath();
}

/** Clip `text` with an ellipsis so it fits `maxWidth` at the current ctx.font. */
function fitText(ctx: CanvasRenderingContext2D, text: string, maxWidth: number): string {
  if (ctx.measureText(text).width <= maxWidth) return text;
  let s = text;
  while (s.length > 0 && ctx.measureText(`${s}…`).width > maxWidth) s = s.slice(0, -1);
  return s.length > 0 ? `${s}…` : '';
}

function setFont(ctx: CanvasRenderingContext2D, size: number, weight = 400) {
  ctx.font = `${weight} ${size}px ${FONT}`;
}

function setLetterSpacing(ctx: CanvasRenderingContext2D, px: number) {
  // Not in every browser's canvas yet; unsupported ones just draw unspaced.
  (ctx as CanvasRenderingContext2D & { letterSpacing?: string }).letterSpacing = `${px}px`;
}

function drawBackground(ctx: CanvasRenderingContext2D) {
  const bg = ctx.createLinearGradient(0, 0, 0, STORY_HEIGHT);
  bg.addColorStop(0, '#0B0F1E');
  bg.addColorStop(1, '#0D0A1C');
  ctx.fillStyle = bg;
  ctx.fillRect(0, 0, STORY_WIDTH, STORY_HEIGHT);

  const glow = (x: number, y: number, r: number, color: string) => {
    const g = ctx.createRadialGradient(x, y, 0, x, y, r);
    g.addColorStop(0, color);
    g.addColorStop(1, 'rgba(0,0,0,0)');
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, STORY_WIDTH, STORY_HEIGHT);
  };
  glow(940, 420, 720, 'rgba(255, 107, 107, 0.20)');
  glow(120, 1500, 760, 'rgba(84, 160, 255, 0.20)');
  glow(540, 960, 900, 'rgba(125, 95, 255, 0.07)');

  // Faint dot grid for texture.
  ctx.fillStyle = 'rgba(255, 255, 255, 0.035)';
  for (let y = 24; y < STORY_HEIGHT; y += 48)
    for (let x = 24; x < STORY_WIDTH; x += 48) ctx.fillRect(x, y, 2, 2);
}

function drawHeader(ctx: CanvasRenderingContext2D, dateLabel: string) {
  ctx.textBaseline = 'alphabetic';
  ctx.textAlign = 'left';

  const accent = ctx.createLinearGradient(PAD, 0, PAD + 360, 0);
  accent.addColorStop(0, '#FF7A6B');
  accent.addColorStop(1, '#7D7BFF');
  setFont(ctx, 26, 700);
  setLetterSpacing(ctx, 8);
  ctx.fillStyle = accent;
  ctx.fillText('MY PORTFOLIO', PAD, 250);
  setLetterSpacing(ctx, 0);

  setFont(ctx, 84, 800);
  ctx.fillStyle = TEXT;
  ctx.fillText('持股配置', PAD, 350);

  setFont(ctx, 30, 500);
  ctx.fillStyle = MUTED;
  ctx.fillText(dateLabel, PAD + 4, 400);
}

function drawDonut(
  ctx: CanvasRenderingContext2D,
  cx: number,
  cy: number,
  section: StorySection,
  colors: string[],
  accent: string,
) {
  const radius = 132;
  const thickness = 44;

  const glow = ctx.createRadialGradient(cx, cy, 0, cx, cy, radius + 90);
  glow.addColorStop(0, `${accent}33`);
  glow.addColorStop(1, `${accent}00`);
  ctx.fillStyle = glow;
  ctx.beginPath();
  ctx.arc(cx, cy, radius + 90, 0, Math.PI * 2);
  ctx.fill();

  ctx.lineWidth = thickness;
  ctx.lineCap = 'butt';
  ctx.strokeStyle = 'rgba(255, 255, 255, 0.06)';
  ctx.beginPath();
  ctx.arc(cx, cy, radius, 0, Math.PI * 2);
  ctx.stroke();

  const gap = section.items.length > 1 ? 0.035 : 0;
  let angle = -Math.PI / 2;
  section.items.forEach((item, i) => {
    const sweep = (item.pct / 100) * Math.PI * 2;
    const start = angle + gap / 2;
    const end = Math.max(start + 0.004, angle + sweep - gap / 2);
    ctx.strokeStyle = colors[i];
    ctx.shadowColor = colors[i];
    ctx.shadowBlur = 18;
    ctx.beginPath();
    ctx.arc(cx, cy, radius, start, end);
    ctx.stroke();
    angle += sweep;
  });
  ctx.shadowBlur = 0;

  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  setFont(ctx, 72, 800);
  ctx.fillStyle = TEXT;
  ctx.fillText(String(section.count), cx, cy - 12);
  setFont(ctx, 24, 500);
  ctx.fillStyle = MUTED;
  ctx.fillText('檔持股', cx, cy + 44);
}

function drawSection(
  ctx: CanvasRenderingContext2D,
  section: StorySection,
  top: number,
  showShare: boolean,
) {
  const theme = THEME[section.market];
  const colors = section.items.map((it, i) =>
    it.isOther ? OTHER_COLOR : theme.palette[i % theme.palette.length],
  );
  const x = PAD;
  const w = STORY_WIDTH - PAD * 2;
  const h = SECTION_HEIGHT;

  // Glass card.
  const card = ctx.createLinearGradient(0, top, 0, top + h);
  card.addColorStop(0, 'rgba(255, 255, 255, 0.07)');
  card.addColorStop(1, 'rgba(255, 255, 255, 0.025)');
  roundRect(ctx, x, top, w, h, 44);
  ctx.fillStyle = card;
  ctx.fill();
  ctx.lineWidth = 2;
  ctx.strokeStyle = 'rgba(255, 255, 255, 0.10)';
  ctx.stroke();

  // Header row: accent bar + market name, and this market's share of all stocks on the right.
  const headerY = top + 76;
  roundRect(ctx, x + 48, headerY - 34, 8, 40, 4);
  ctx.fillStyle = theme.accent;
  ctx.fill();

  ctx.textBaseline = 'alphabetic';
  ctx.textAlign = 'left';
  setFont(ctx, 44, 800);
  ctx.fillStyle = TEXT;
  ctx.fillText(theme.label, x + 76, headerY);
  const labelW = ctx.measureText(theme.label).width;
  setFont(ctx, 20, 700);
  setLetterSpacing(ctx, 5);
  ctx.fillStyle = MUTED;
  ctx.fillText(theme.sub, x + 76 + labelW + 20, headerY - 4);
  setLetterSpacing(ctx, 0);

  if (showShare) {
    ctx.textAlign = 'right';
    setFont(ctx, 40, 800);
    ctx.fillStyle = theme.accent;
    const shareText = `${section.shareOfStocks.toFixed(1)}%`;
    ctx.fillText(shareText, x + w - 48, headerY);
    const shareW = ctx.measureText(shareText).width;
    setFont(ctx, 22, 500);
    ctx.fillStyle = MUTED;
    ctx.fillText('佔股票', x + w - 48 - shareW - 14, headerY - 2);
  }

  ctx.fillStyle = 'rgba(255, 255, 255, 0.08)';
  ctx.fillRect(x + 48, top + 122, w - 96, 2);

  // Body: donut on the left, ranked list on the right.
  const bodyTop = top + 124;
  const bodyBottom = top + h - 28;
  const bodyMid = (bodyTop + bodyBottom) / 2;
  drawDonut(ctx, x + 220, bodyMid, section, colors, theme.accent);

  const listX = x + 432;
  const listRight = x + w - 48;
  const rowH = Math.min(72, (bodyBottom - bodyTop - 24) / section.items.length);
  let rowY = bodyMid - (rowH * section.items.length) / 2;

  section.items.forEach((item, i) => {
    const textY = rowY + rowH / 2 - 2;
    ctx.textBaseline = 'middle';

    ctx.fillStyle = colors[i];
    ctx.beginPath();
    ctx.arc(listX + 8, textY, 8, 0, Math.PI * 2);
    ctx.fill();

    ctx.textAlign = 'right';
    setFont(ctx, 32, 800);
    ctx.fillStyle = TEXT;
    const pctText = `${item.pct.toFixed(1)}%`;
    ctx.fillText(pctText, listRight, textY);
    const pctW = ctx.measureText(pctText).width;

    ctx.textAlign = 'left';
    const nameStart = listX + 30;
    const nameMax = listRight - pctW - 20 - nameStart;
    setFont(ctx, 30, 700);
    ctx.fillStyle = TEXT;
    const symbol = fitText(ctx, item.symbol, nameMax);
    ctx.fillText(symbol, nameStart, textY);
    const symbolW = ctx.measureText(symbol).width;
    if (item.name && item.name !== item.symbol) {
      setFont(ctx, 24, 500);
      ctx.fillStyle = MUTED;
      ctx.fillText(
        fitText(ctx, item.name, nameMax - symbolW - 12),
        nameStart + symbolW + 12,
        textY,
      );
    }

    // Thin bar under the row, full width = 100% of this market.
    const barY = textY + rowH / 2 - 10;
    const barW = listRight - nameStart;
    roundRect(ctx, nameStart, barY, barW, 5, 2.5);
    ctx.fillStyle = 'rgba(255, 255, 255, 0.06)';
    ctx.fill();
    roundRect(ctx, nameStart, barY, Math.max(5, (barW * item.pct) / 100), 5, 2.5);
    ctx.fillStyle = colors[i];
    ctx.fill();

    rowY += rowH;
  });
}

/**
 * Draw the 1080×1920 story: TW on the top half, US on the bottom half, so cropping the bottom
 * half away leaves a self-contained TW-only image (each half sums to 100% on its own).
 */
export async function renderStoryImage(
  alloc: StoryAllocation,
  dateLabel: string,
): Promise<HTMLCanvasElement> {
  await document.fonts?.ready;
  const canvas = document.createElement('canvas');
  canvas.width = STORY_WIDTH;
  canvas.height = STORY_HEIGHT;
  const ctx = canvas.getContext('2d');
  if (!ctx) throw new Error('Canvas 2D is not supported');

  drawBackground(ctx);
  drawHeader(ctx, dateLabel);

  const sections = [alloc.tw, alloc.us].filter((s): s is StorySection => s != null);
  const both = sections.length === 2;
  if (both) {
    drawSection(ctx, sections[0], CONTENT_TOP, true);
    drawSection(ctx, sections[1], CONTENT_TOP + SECTION_HEIGHT + SECTION_GAP, true);
  } else if (sections.length === 1) {
    // Lone market: centre it in the content area; a 100% share line would say nothing.
    drawSection(ctx, sections[0], (CONTENT_TOP + CONTENT_BOTTOM - SECTION_HEIGHT) / 2, false);
  }
  return canvas;
}

export function canvasToPngBlob(canvas: HTMLCanvasElement): Promise<Blob> {
  return new Promise((resolve, reject) =>
    canvas.toBlob((b) => (b ? resolve(b) : reject(new Error('toBlob failed'))), 'image/png'),
  );
}
