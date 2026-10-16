import ClearAllIcon from '@mui/icons-material/ClearAll';
import DeleteOutlinedIcon from '@mui/icons-material/DeleteOutlined';
import RestartAltIcon from '@mui/icons-material/RestartAlt';
import TimelineIcon from '@mui/icons-material/Timeline';
import UndoIcon from '@mui/icons-material/Undo';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';
import Typography from '@mui/material/Typography';
import { useCallback, useLayoutEffect, useMemo, useRef, useState } from 'react';

import '../charts/registerFinanceCharts';
import {
  buildTrendlineOption,
  chartPriceRange,
  distancePct,
  fmtPrice,
  RESISTANCE_COLOR,
  SUPPORT_COLOR,
} from '../utils/trendlineChartOption';
import {
  autoDrawnLines,
  clearDrawings,
  currentLevels,
  lineGeometry,
  loadDrawings,
  saveDrawings,
  type CurrentLevel,
  type DrawnLine,
  type LineGeometry,
} from '../utils/trendlineDrawings';
import {
  availablePresets,
  latestStart,
  loadRange,
  rangeStartIndex,
  saveRange,
  type RangeChoice,
  type RangePresetId,
} from '../utils/trendlineRange';
import { analyzeTrendlines } from '../utils/trendlines';

import {
  measureFrame,
  projectionOf,
  sameFrame,
  type ChartFrame,
  type PixelConverter,
} from './trendlineEditor/chartProjection';
import { TrendlineOverlay } from './trendlineEditor/TrendlineOverlay';

import type { PriceBarsData } from '@/apis/financeApis/types';

import { ECHARTS_THEME } from '@/charts/echarts';
import ReactECharts from '@/charts/ReactECharts';
import { InfoPopover } from '@/components/hints';

export const TRENDLINE_METHOD =
  '線是程式依規則自動畫的，沒有人工挑日期：先用 ZigZag 濾掉小於 2 倍 ATR（平均真實波幅）的小波動，' +
  '只留下真正的波段高低點；再把每兩個高點（或低點）都試著連成一條線，挑「被波段點碰觸最多、收盤最少穿越、' +
  '離價格最近」的那條。每條線至少連 2 個點，畫到被連續兩日收盤穿越（或單日穿越 1 倍 ATR），' +
  '或價格離開超過 3 倍 ATR 為止。線只從選的起始日期之後的 K 棒找，所以換起始日期會畫出不同的線。' +
  '藍色是支撐、紫色是壓力（多數收盤在線上方就是支撐），粗線是今天仍有效的線。' +
  '線可以自己拖曳、刪除或新畫，改過的線存在這個瀏覽器。';

export const TRENDLINE_DISCLAIMER = '趨勢線僅供參考，不構成投資建議。';

const CHART_HEIGHT = 360;
/** Undo steps kept. */
const UNDO_DEPTH = 20;

export const TRENDLINE_CHART_HEIGHT = CHART_HEIGHT;

function LevelChip({
  label,
  color,
  current,
  lastClose,
}: {
  label: string;
  color: string;
  current: CurrentLevel | null;
  lastClose: number;
}) {
  return (
    <Chip
      size="small"
      variant="outlined"
      // Wraps instead of cutting the distance off on a phone.
      sx={{
        borderColor: color,
        color,
        fontWeight: 600,
        height: 'auto',
        minHeight: 24,
        '& .MuiChip-label': { whiteSpace: 'normal', py: 0.25 },
      }}
      label={
        current == null
          ? `${label}：目前沒有有效的線`
          : `${label} ≈ ${fmtPrice(current.level)}（連 ${current.touches} 點，收盤距離 ${distancePct(lastClose, current.level)}）`
      }
    />
  );
}

interface EChartsHandle {
  getEchartsInstance: () => PixelConverter;
}

export interface TrendlineChartProps {
  data: PriceBarsData;
  /** Where the chart starts until the user picks a start date. */
  defaultRange: RangePresetId;
}

/**
 * Candlesticks with support/resistance trendlines, shared by the TAIEX card and the stock
 * analysis. The lines start out automatic (see {@link TRENDLINE_METHOD}); the user can pick the
 * start date, drag, delete and draw lines, and both choices are kept per code in this browser.
 */
export function TrendlineChart(props: TrendlineChartProps) {
  // Another code is another drawing: start its state afresh.
  return <EditableTrendlineChart key={props.data.code} {...props} />;
}

function EditableTrendlineChart({ data, defaultRange }: TrendlineChartProps) {
  const { code } = data;
  const allBars = data.bars;
  const allDates = useMemo(() => allBars.map((b) => b.date), [allBars]);

  // ── Start date ──
  const [range, setRange] = useState<RangeChoice>(
    () => loadRange(code) ?? { preset: defaultRange },
  );
  const presets = useMemo(() => availablePresets(allDates), [allDates]);
  const start = rangeStartIndex(allDates, range);
  const bars = useMemo(() => allBars.slice(start), [allBars, start]);
  const dates = useMemo(() => bars.map((b) => b.date), [bars]);
  const lastClose = bars[bars.length - 1].close;
  const shownPreset =
    'preset' in range ? (presets.some((p) => p.id === range.preset) ? range.preset : 'all') : null;

  // ── Lines ──
  const analysis = useMemo(() => analyzeTrendlines(bars), [bars]);
  const autoLines = useMemo(() => autoDrawnLines(bars, analysis), [bars, analysis]);
  const [custom, setCustom] = useState<DrawnLine[] | null>(() => loadDrawings(code));
  const [past, setPast] = useState<(DrawnLine[] | null)[]>([]);
  const lines = custom ?? autoLines;
  const measure = useCallback(
    (line: DrawnLine) => lineGeometry(bars, analysis, line),
    [bars, analysis],
  );
  const geometries = useMemo(
    () => lines.map(measure).filter((g): g is LineGeometry => g !== null),
    [lines, measure],
  );
  const levels = currentLevels(geometries, lastClose);

  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [drawing, setDrawing] = useState(false);
  const selected = geometries.find((g) => g.id === selectedId) ?? null;

  const store = (next: DrawnLine[] | null) => {
    setCustom(next);
    if (next === null) clearDrawings(code);
    else saveDrawings(code, next);
  };
  /** Every change goes through here, so it can be undone. */
  const apply = (next: DrawnLine[] | null) => {
    setPast((p) => [...p.slice(1 - UNDO_DEPTH), custom]);
    // A line wholly before the first bar of the history can never show again: let it go. One
    // that only falls before the chosen start date stays, for when the chart starts earlier.
    store(next && next.filter((l) => lineGeometry(allBars, { atrs: [], swings: [] }, l) !== null));
  };
  const undo = () => {
    store(past[past.length - 1]);
    setPast((p) => p.slice(0, -1));
    setSelectedId(null);
  };

  const changeLine = (line: DrawnLine) => apply(lines.map((l) => (l.id === line.id ? line : l)));
  const deleteLine = (id: string) => {
    apply(lines.filter((l) => l.id !== id));
    setSelectedId(null);
  };
  const addLine = (line: DrawnLine) => {
    apply([...lines, line]);
    setDrawing(false);
    setSelectedId(line.id);
  };

  const pickRange = (next: RangeChoice) => {
    setRange(next);
    saveRange(code, next);
    setSelectedId(null);
    setDrawing(false);
  };

  // ── Chart geometry for the overlay ──
  const priceRange = useMemo(() => chartPriceRange(bars), [bars]);
  const option = useMemo(() => buildTrendlineOption(bars, priceRange), [bars, priceRange]);
  const chartRef = useRef<EChartsHandle | null>(null);
  const [frame, setFrame] = useState<ChartFrame | null>(null);
  const remeasure = useRef(() => {});
  // ECharts lays the chart out synchronously when it gets the option, which happens in the
  // chart's own commit, before this effect: so every render can measure what is on screen. A
  // resize re-lays it out without a render here, hence also the `finished` event below.
  useLayoutEffect(() => {
    remeasure.current = () => {
      const chart = chartRef.current?.getEchartsInstance();
      if (!chart) return;
      const next = measureFrame(chart, dates, priceRange);
      setFrame((prev) => (sameFrame(prev, next) ? prev : next));
    };
    remeasure.current();
  });
  // Stable, or echarts-for-react would rebuild the chart on every render. echarts-for-react
  // sets the option only once the chart's box has a size, so the first measure that can succeed
  // is when it reports the chart ready.
  const onEvents = useMemo(() => ({ finished: () => remeasure.current() }), []);
  const onChartReady = useCallback(() => remeasure.current(), []);
  const projection = useMemo(() => (frame ? projectionOf(frame) : null), [frame]);

  const hint = drawing
    ? '在圖上點起點和終點（或按住拖曳）畫一條線，靠近 K 棒最高、最低點會自動對齊；按 Esc 取消。'
    : selected
      ? '拖曳圓點調整端點、拖曳線段整條移動；鍵盤可用方向鍵微調、Delete 刪除。'
      : '點一下線就能拖曳或刪除，按「畫線」可以自己畫。';

  return (
    <Box
      onKeyDown={(e) => {
        if (e.key !== 'Escape') return;
        if (drawing) setDrawing(false);
        else setSelectedId(null);
      }}
    >
      <Stack
        direction="row"
        spacing={1}
        sx={{ flexWrap: 'wrap', rowGap: 1, mb: 1.5, alignItems: 'center' }}
      >
        <ToggleButtonGroup
          size="small"
          exclusive
          value={shownPreset}
          onChange={(_, id: RangePresetId | null) => id && pickRange({ preset: id })}
          aria-label="圖表區間"
          sx={{ flexWrap: 'wrap' }}
        >
          {presets.map((p) => (
            <ToggleButton
              key={p.id}
              value={p.id}
              sx={{ px: 0.75, py: 0.25, minWidth: 0, fontSize: '0.8125rem' }}
            >
              {p.label}
            </ToggleButton>
          ))}
        </ToggleButtonGroup>
        <TextField
          type="date"
          size="small"
          label="起始日期"
          value={dates[0]}
          onChange={(e) => {
            if (/^\d{4}-\d{2}-\d{2}$/.test(e.target.value)) pickRange({ from: e.target.value });
          }}
          slotProps={{
            inputLabel: { shrink: true },
            htmlInput: { min: allDates[0], max: latestStart(allDates) },
          }}
          sx={{ width: 160 }}
        />
      </Stack>

      <Stack
        direction="row"
        spacing={1}
        sx={{ flexWrap: 'wrap', rowGap: 1, mb: 1, alignItems: 'center' }}
      >
        <LevelChip
          label="支撐"
          color={SUPPORT_COLOR}
          current={levels.support}
          lastClose={lastClose}
        />
        <LevelChip
          label="壓力"
          color={RESISTANCE_COLOR}
          current={levels.resistance}
          lastClose={lastClose}
        />
        <InfoPopover content={TRENDLINE_METHOD} ariaLabel="趨勢線怎麼畫的" iconSize={16} />
      </Stack>

      <Stack
        direction="row"
        spacing={0.5}
        sx={{ flexWrap: 'wrap', rowGap: 0.5, alignItems: 'center' }}
      >
        <Button
          size="small"
          variant={drawing ? 'contained' : 'outlined'}
          startIcon={<TimelineIcon />}
          aria-pressed={drawing}
          onClick={() => {
            setDrawing((d) => !d);
            setSelectedId(null);
          }}
        >
          {drawing ? '取消畫線' : '畫線'}
        </Button>
        {selected && !drawing && (
          <Button
            size="small"
            color="error"
            startIcon={<DeleteOutlinedIcon />}
            onClick={() => deleteLine(selected.id)}
          >
            刪除這條線
          </Button>
        )}
        {past.length > 0 && (
          <Button size="small" startIcon={<UndoIcon />} onClick={undo}>
            復原
          </Button>
        )}
        {lines.length > 0 && (
          <Button
            size="small"
            startIcon={<ClearAllIcon />}
            onClick={() => {
              apply([]);
              setSelectedId(null);
            }}
          >
            清除全部
          </Button>
        )}
        {custom !== null && (
          <Button
            size="small"
            startIcon={<RestartAltIcon />}
            onClick={() => {
              apply(null);
              setSelectedId(null);
              setDrawing(false);
            }}
          >
            還原自動線
          </Button>
        )}
      </Stack>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 0.5 }}>
        {hint}
        {custom !== null && '這張圖的線是你調整過的版本，存在這個瀏覽器，不會再隨新資料自動重畫。'}
      </Typography>

      <Box
        sx={{ position: 'relative', height: CHART_HEIGHT }}
        onPointerDown={() => {
          if (!drawing) setSelectedId(null);
        }}
      >
        <Box
          role="img"
          aria-label={`${data.name} 日K線，${dates[0]} 起，共 ${geometries.length} 條趨勢線`}
        >
          <ReactECharts
            ref={chartRef as never}
            option={option}
            notMerge
            theme={ECHARTS_THEME}
            onEvents={onEvents}
            onChartReady={onChartReady}
            style={{ height: CHART_HEIGHT, width: '100%' }}
          />
        </Box>
        {projection && (
          <TrendlineOverlay
            bars={bars}
            lines={geometries}
            projection={projection}
            measure={measure}
            selectedId={selectedId}
            drawing={drawing}
            onSelect={setSelectedId}
            onChange={changeLine}
            onDelete={deleteLine}
            onCreate={addLine}
          />
        )}
      </Box>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
        {dates[0]} 起，資料至 {data.asOf}，收盤 {fmtPrice(lastClose)}。{TRENDLINE_DISCLAIMER}
      </Typography>
    </Box>
  );
}
