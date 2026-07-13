import Box from '@mui/material/Box';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';

interface StatCardProps {
  label: string;
  value: React.ReactNode;
  hint?: string;
  loading?: boolean;
}

function StatCard({ label, value, hint, loading }: StatCardProps) {
  const card = (
    <Paper variant="outlined" sx={{ px: 2, py: 1.25, minWidth: 140, flex: '0 1 auto' }}>
      <Typography
        variant="caption"
        sx={{ color: 'text.secondary', display: 'block', lineHeight: 1.6 }}
      >
        {label}
      </Typography>
      {loading ? (
        <Skeleton width={72} height={28} />
      ) : (
        <Typography variant="h6" sx={{ lineHeight: 1.3 }}>
          {value}
        </Typography>
      )}
    </Paper>
  );
  return hint ? (
    <Tooltip title={hint} placement="top">
      {card}
    </Tooltip>
  ) : (
    card
  );
}

interface OverviewStripProps {
  /** Pre-formatted row count, e.g. "1,234" or "200+"; undefined while unknown. */
  rowCountDisplay: string | undefined;
  rowCountLoading: boolean;
  columnCount: number;
  schemaLoading: boolean;
  dateField: string;
  maxDate: string | null | undefined;
  maxDateLoading: boolean;
}

/**
 * Compact at-a-glance stats for the active data source: row count, column
 * count, and the most recent date. Absorbs what used to be the Max Date tab.
 */
export function OverviewStrip({
  rowCountDisplay,
  rowCountLoading,
  columnCount,
  schemaLoading,
  dateField,
  maxDate,
  maxDateLoading,
}: OverviewStripProps) {
  return (
    <Box sx={{ display: 'flex', gap: 1.5, flexWrap: 'wrap', mb: 2 }}>
      <StatCard label="資料筆數" value={rowCountDisplay ?? '—'} loading={rowCountLoading} />
      <StatCard
        label="欄位數"
        value={columnCount > 0 ? columnCount : '—'}
        loading={schemaLoading}
      />
      <StatCard
        label={`最新 ${dateField || '日期'}`}
        value={
          maxDate
            ? new Date(maxDate).toLocaleDateString('en-US', {
                year: 'numeric',
                month: 'short',
                day: 'numeric',
              })
            : '—'
        }
        loading={maxDateLoading}
        hint={`MAX(${dateField}) — 可在 Schema 分頁選擇其他日期欄位`}
      />
    </Box>
  );
}
