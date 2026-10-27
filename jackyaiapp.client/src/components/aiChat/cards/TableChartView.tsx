import { Box, ToggleButton, ToggleButtonGroup, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { useMemo, useState } from 'react';

import {
  TABLE_CHART_LABELS,
  firstValueKey,
  leftOutNotice,
  tableChartData,
  tableChartHeight,
  tableChartKinds,
  tableChartOption,
  type TableChartKind,
  type TableChartPlan,
} from './tableChart';

import type { TableCard } from './toolCard';

import ReactECharts from '@/charts/ReactECharts';

const pickerSx = {
  flexWrap: 'wrap',
  '& .MuiToggleButton-root': { py: 0.25, px: 1, fontSize: 12, lineHeight: 1.6 },
} as const;

/**
 * 畫成圖 (D-153): the table card's rows on screen as a chart. Which column (one at a time: 市值 and 損益 % have different
 * scales) and which chart are picked right here, among those that fit. Loaded only when the reader asks for it, so the
 * chat's own chunk carries no ECharts.
 */
export default function TableChartView({
  card,
  rows,
  plan,
  sortedKey,
}: {
  card: TableCard;
  rows: TableCard['rows'];
  plan: TableChartPlan;
  sortedKey: string | null;
}) {
  const theme = useTheme();
  const [pickedKey, setPickedKey] = useState<string | null>(null);
  const [pickedKind, setPickedKind] = useState<TableChartKind | null>(null);
  const key =
    pickedKey !== null && plan.values.some((c) => c.key === pickedKey)
      ? pickedKey
      : firstValueKey(plan, sortedKey);
  const kinds = useMemo(() => tableChartKinds(card, plan, key), [card, plan, key]);
  const kind = pickedKind !== null && kinds.includes(pickedKind) ? pickedKind : kinds[0];
  const column = plan.values.find((c) => c.key === key)!;
  const data = useMemo(() => tableChartData(rows, plan, key, kind), [rows, plan, key, kind]);
  const option = useMemo(
    () =>
      tableChartOption(data, kind, column.label, {
        text: theme.palette.text.secondary,
        line: theme.palette.divider,
        accent: theme.palette.primary.main,
        surface: theme.palette.background.paper,
        ink: theme.palette.text.primary,
      }),
    [data, kind, column.label, theme],
  );
  const notice = leftOutNotice(data, kind);

  return (
    <Box data-testid="table-chart">
      {(plan.values.length > 1 || kinds.length > 1) && (
        <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1, mb: 1 }}>
          {plan.values.length > 1 && (
            <ToggleButtonGroup
              size="small"
              exclusive
              value={key}
              onChange={(_e, v: string | null) => v !== null && setPickedKey(v)}
              aria-label="畫哪一欄"
              sx={pickerSx}
            >
              {plan.values.map((c) => (
                <ToggleButton key={c.key} value={c.key}>
                  {c.label}
                </ToggleButton>
              ))}
            </ToggleButtonGroup>
          )}
          {kinds.length > 1 && (
            <ToggleButtonGroup
              size="small"
              exclusive
              value={kind}
              onChange={(_e, v: TableChartKind | null) => v !== null && setPickedKind(v)}
              aria-label="換一種圖表"
              sx={pickerSx}
            >
              {kinds.map((k) => (
                <ToggleButton key={k} value={k}>
                  {TABLE_CHART_LABELS[k]}
                </ToggleButton>
              ))}
            </ToggleButtonGroup>
          )}
        </Box>
      )}
      <Box
        role="img"
        aria-label={`${column.label}的${TABLE_CHART_LABELS[kind]}`}
        sx={{ height: tableChartHeight(data, kind) }}
      >
        <ReactECharts option={option} notMerge style={{ height: '100%', width: '100%' }} />
      </Box>
      {notice && (
        <Typography variant="caption" color="text.secondary">
          {notice}
        </Typography>
      )}
    </Box>
  );
}
