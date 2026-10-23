import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import { useCallback, useMemo, useState } from 'react';

import '../../charts/registerFinanceCharts';
import {
  buildTrendlineOption,
  chartPriceRange,
  fmtPrice,
  hasVolume,
  trendlineChartHeight,
  type VolumeUnit,
} from '../../utils/trendlineChartOption';
import {
  autoDrawnLines,
  currentLevels,
  lineGeometry,
  lineValuesAt,
  withSide,
  type DrawnLine,
  type LineGeometry,
} from '../../utils/trendlineDrawings';
import { type RangeChoice, type RangePresetId } from '../../utils/trendlineRange';
import { analyzeTrendlines, type TrendlineSide } from '../../utils/trendlines';
import { TrendlineOverlay } from '../trendlineEditor/TrendlineOverlay';

import { lineHint, TRENDLINE_DISCLAIMER } from './copy';
import { LevelChips } from './LevelChips';
import { LineToolbar } from './LineToolbar';
import { RangeControls } from './RangeControls';
import { useChartFrame } from './useChartFrame';
import { useLineDrawings } from './useLineDrawings';
import { useRangeChoice } from './useRangeChoice';

import type { PriceBarsData } from '@/apis/financeApis/types';

import { ECHARTS_THEME } from '@/charts/echarts';
import ReactECharts from '@/charts/ReactECharts';

export { TRENDLINE_DISCLAIMER, TRENDLINE_METHOD } from './copy';
export { TRENDLINE_CHART_HEIGHT } from './layout';

export interface TrendlineChartProps {
  data: PriceBarsData;
  /** Where the chart starts until the user picks a start date. */
  defaultRange: RangePresetId;
  /** How the market counts volume; without it (the TAIEX) there is no volume pane. */
  volume?: VolumeUnit;
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

function EditableTrendlineChart({ data, defaultRange, volume }: TrendlineChartProps) {
  const { code } = data;
  const allBars = data.bars;
  const allDates = useMemo(() => allBars.map((b) => b.date), [allBars]);

  // ── Start date ──
  const range = useRangeChoice(code, allDates, defaultRange);
  const { start } = range;
  const bars = useMemo(() => allBars.slice(start), [allBars, start]);
  const dates = useMemo(() => bars.map((b) => b.date), [bars]);
  const lastClose = bars[bars.length - 1].close;

  // ── Lines ──
  const analysis = useMemo(() => analyzeTrendlines(bars), [bars]);
  const autoLines = useMemo(() => autoDrawnLines(bars, analysis), [bars, analysis]);
  const drawings = useLineDrawings(code, allBars, autoLines);
  const { lines } = drawings;
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
  /** The kind of line being drawn, if any. */
  const [drawing, setDrawing] = useState<TrendlineSide | null>(null);
  const selected = geometries.find((g) => g.id === selectedId) ?? null;

  const deleteLine = (id: string) => {
    drawings.remove(id);
    setSelectedId(null);
  };
  const addLine = (line: DrawnLine) => {
    drawings.add(line);
    setDrawing(null);
    setSelectedId(line.id);
  };
  const undo = () => {
    drawings.undo();
    setSelectedId(null);
  };
  const pickRange = (next: RangeChoice) => {
    range.pick(next);
    setSelectedId(null);
    setDrawing(null);
  };

  // ── Chart geometry for the overlay ──
  const priceRange = useMemo(() => chartPriceRange(bars), [bars]);
  // The tooltip lists the lines over the hovered day, so the option follows the lines. A drag only
  // moves the overlay's preview; the chart gets a new option once, when the line is let go.
  const option = useMemo(
    () =>
      buildTrendlineOption(bars, priceRange, {
        linesAt: (index) => lineValuesAt(geometries, index),
        volume,
      }),
    [bars, priceRange, geometries, volume],
  );
  const chartHeight = trendlineChartHeight(hasVolume(bars, volume));
  const { chartRef, onEvents, onChartReady, projection } = useChartFrame(dates, priceRange);

  return (
    <Box
      onKeyDown={(e) => {
        if (e.key !== 'Escape') return;
        if (drawing) setDrawing(null);
        else setSelectedId(null);
      }}
    >
      <RangeControls
        presets={range.presets}
        shownPreset={range.shownPreset}
        startDate={dates[0]}
        allDates={allDates}
        onPick={pickRange}
      />

      <LevelChips support={levels.support} resistance={levels.resistance} lastClose={lastClose} />

      <LineToolbar
        drawing={drawing}
        onDraw={(side) => {
          setDrawing(side);
          setSelectedId(null);
        }}
        onCancelDrawing={() => setDrawing(null)}
        selectedSide={selected?.side ?? null}
        onSwapSide={(side) => selected && drawings.change(withSide(bars, selected, side))}
        onDeleteSelected={() => selected && deleteLine(selected.id)}
        canUndo={drawings.canUndo}
        onUndo={undo}
        hasLines={lines.length > 0}
        onClear={() => {
          drawings.clear();
          setSelectedId(null);
        }}
        customized={drawings.customized}
        onRestoreAutomatic={() => {
          drawings.restoreAutomatic();
          setSelectedId(null);
          setDrawing(null);
        }}
      />
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 0.5 }}>
        {lineHint(drawing, selected !== null)}
        {drawings.customized &&
          '這張圖的線是你調整過的版本，存在這個瀏覽器，不會再隨新資料自動重畫。'}
      </Typography>

      <Box
        sx={{ position: 'relative', height: chartHeight }}
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
            style={{ height: chartHeight, width: '100%' }}
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
            onChange={drawings.change}
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
