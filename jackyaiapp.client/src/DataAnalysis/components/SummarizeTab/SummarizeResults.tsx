import CodeIcon from '@mui/icons-material/Code';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Collapse from '@mui/material/Collapse';
import IconButton from '@mui/material/IconButton';
import Paper from '@mui/material/Paper';
import Typography from '@mui/material/Typography';

import { VizSwitcher } from '../../viz/VizSwitcher';

import { ExampleChips } from './ExampleChips';
import { ColumnHints, SummarizeMode } from './summarizeResult';

import type { SummarizeRequest } from '../../../apis/dataAnalysisApis/types';

import { ActionTooltip } from '@/components/hints';

interface SummarizeResultsProps extends ColumnHints {
  mode: SummarizeMode;
  rows: Record<string, unknown>[];
  headers: string[];
  nlInterpretation?: string;
  nlPlan?: SummarizeRequest;
  nlNotes?: string[];
  planOpen: boolean;
  onTogglePlan: () => void;
  onPickExample: (question: string) => void;
  /** Changes per result, so the chart is remounted and its UI state reset. */
  resultKey: string;
  currentPlan: SummarizeRequest | null;
  canAddToDashboard: boolean;
}

/** The current result: its row count, the AI's reading of the question and plan, and the chart —
 * or, when nothing came back, what to try instead. */
export function SummarizeResults({
  mode,
  rows,
  headers,
  nlInterpretation,
  nlPlan,
  nlNotes,
  planOpen,
  onTogglePlan,
  onPickExample,
  resultKey,
  groupHints,
  temporalHints,
  currentPlan,
  canAddToDashboard,
}: SummarizeResultsProps) {
  return (
    <>
      {/* ── Results header ── */}
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          marginBottom: 8,
        }}
      >
        <Box>
          <Typography variant="caption" color="text.secondary">
            回傳 {rows.length} 筆資料
          </Typography>
          {mode === 'ai' && nlInterpretation && (
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.25 }}>
              <Typography variant="caption" color="text.secondary" sx={{ fontStyle: 'italic' }}>
                {nlInterpretation}
              </Typography>
              {nlPlan && (
                <ActionTooltip title="檢視原始查詢計畫">
                  <IconButton size="small" onClick={onTogglePlan} sx={{ p: 0.25 }}>
                    <CodeIcon sx={{ fontSize: 14 }} />
                  </IconButton>
                </ActionTooltip>
              )}
            </Box>
          )}
        </Box>
      </div>

      {/* ── Raw query plan (developer detail, toggled from the code icon) ── */}
      {mode === 'ai' && nlPlan && (
        <Collapse in={planOpen}>
          <Paper variant="outlined" sx={{ p: 1.5, mb: 1.5, overflow: 'auto' }}>
            <Box
              component="pre"
              sx={{
                m: 0,
                fontSize: '0.72rem',
                fontFamily: 'monospace',
                color: 'text.secondary',
              }}
            >
              {JSON.stringify(nlPlan, null, 2)}
            </Box>
          </Paper>
        </Collapse>
      )}

      {/* ── Dropped-filter notes (rows came back, but a requested value didn't exist) ── */}
      {mode === 'ai' && nlNotes && nlNotes.length > 0 && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          {nlNotes.map((note) => (
            <div key={note}>{note}</div>
          ))}
        </Alert>
      )}

      {/* ── Empty state ── */}
      {headers.length === 0 && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          <Typography variant="body2">
            找不到符合此問題的資料——你詢問的日期或數值可能不存在於此資料中。試試看以下範例：
          </Typography>
          <ExampleChips onPick={onPickExample} spacing={{ mt: 1 }} />
        </Alert>
      )}

      {/* ── Visualization: switch between any viz whose constraints fit the result ── */}
      {headers.length > 0 && (
        <VizSwitcher
          key={resultKey}
          rows={rows}
          headers={headers}
          groupHints={groupHints}
          temporalHints={temporalHints}
          currentPlan={currentPlan}
          canAddToDashboard={canAddToDashboard}
        />
      )}
    </>
  );
}
