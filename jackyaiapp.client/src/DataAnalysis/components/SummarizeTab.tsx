import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import CheckIcon from '@mui/icons-material/Check';
import CodeIcon from '@mui/icons-material/Code';
import ExpandLessIcon from '@mui/icons-material/ExpandLess';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import HistoryIcon from '@mui/icons-material/History';
import LoginIcon from '@mui/icons-material/Login';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import SettingsIcon from '@mui/icons-material/Settings';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import Collapse from '@mui/material/Collapse';
import Divider from '@mui/material/Divider';
import IconButton from '@mui/material/IconButton';
import Paper from '@mui/material/Paper';
import TextField from '@mui/material/TextField';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';
import Typography from '@mui/material/Typography';
import { useEffect, useMemo, useState } from 'react';

import { VizSwitcher } from '../viz/VizSwitcher';

import { FieldTransformEditor } from './FieldTransformEditor';
import { FilterEditor } from './FilterEditor';
import { MeasureEditor } from './MeasureEditor';
import { ViewByEditor } from './ViewByEditor';

import type {
  FieldDefinition,
  MeasureFieldDto,
  SummarizeRequest,
} from '../../apis/dataAnalysisApis/types';
import type { ActiveFilter, FieldTransformDef, ViewByEntry } from '../types';

import { ActionTooltip } from '@/components/hints';

type SummarizeMode = 'manual' | 'ai';

interface SummarizeTabProps {
  // Transforms
  fieldTransforms: FieldTransformDef[];
  onTransformAdd: (t: Omit<FieldTransformDef, 'id'>) => void;
  onTransformUpdate: (t: FieldTransformDef) => void;
  onTransformRemove: (id: number) => void;
  // ViewBy
  viewByEntries: ViewByEntry[];
  onViewByAdd: () => void;
  onViewByUpdate: (entry: ViewByEntry) => void;
  onViewByRemove: (id: number) => void;
  // Measures
  measures: MeasureFieldDto[];
  onMeasureAdd: () => void;
  onMeasureRemove: (i: number) => void;
  onMeasureUpdate: (i: number, patch: Partial<MeasureFieldDto>) => void;
  // Filters
  summarizeFilters: ActiveFilter[];
  onSummarizeFiltersChange: (filters: ActiveFilter[]) => void;
  // Run (manual)
  onRunSummarize: () => void;
  isLoading: boolean;
  error: unknown;
  // Results (manual)
  summarizeRows: Record<string, unknown>[];
  summarizeHeaders: string[];
  // NL mode
  onRunNlSummarize: (query: string) => void;
  nlIsLoading: boolean;
  nlError: unknown;
  nlRows: Record<string, unknown>[];
  nlInterpretation?: string;
  nlPlan?: SummarizeRequest;
  nlNotes?: string[];
  /** Bumped per AI / manual result so the result view can be remounted to reset its UI state. */
  nlResultVersion: number;
  summarizeVersion: number;
  /** Re-runnable plan behind the current manual result (null in session mode). */
  summarizeLastRequest: SummarizeRequest | null;
  /** Whether "Add to dashboard" is available (logged in, persistent source — not a session). */
  canAddToDashboard: boolean;
  isLoggedIn: boolean;
  // Context
  fields: FieldDefinition[];
  sourceId?: string;
  tableName?: string;
}

// Mix of starter and realistic "what a sales/finance team would actually ask"
// questions — the complex ones exercise multi-dimension group-bys, filters,
// NumericBin tiers, and weekly DateTrunc with relative dates.
// Values and tier boundaries verified against the live dbo.Orders data
// (Revenue spans ~1K–10K; categories are Consulting/Hardware/Software/
// Support/Training; statuses are Completed/Pending/Cancelled).
const NL_EXAMPLES = [
  '各地區總營收',
  '每月訂單數量趨勢',
  'EMEA 地區企業客戶各銷售管道的季營收',
  '依成交金額級距（<3K、3K–6K、>6K）統計訂單數與營收',
  '今年 Software 類別已完成訂單的每週營收',
];

// Staged progress shown while the NL agent runs. Mirrors the backend's real
// phases (plan → fetch live values → execute); timing is simulated locally
// since the request is a single HTTP call.
const NL_STEPS = ['理解你的問題', '檢查資料中的實際數值', '執行查詢'];

// ── Component ─────────────────────────────────────────────────────────────────

export function SummarizeTab({
  fieldTransforms,
  onTransformAdd,
  onTransformUpdate,
  onTransformRemove,
  viewByEntries,
  onViewByAdd,
  onViewByUpdate,
  onViewByRemove,
  measures,
  onMeasureAdd,
  onMeasureRemove,
  onMeasureUpdate,
  summarizeFilters,
  onSummarizeFiltersChange,
  onRunSummarize,
  isLoading,
  error,
  summarizeRows,
  summarizeHeaders,
  onRunNlSummarize,
  nlIsLoading,
  nlError,
  nlRows,
  nlInterpretation,
  nlPlan,
  nlNotes,
  nlResultVersion,
  summarizeVersion,
  summarizeLastRequest,
  canAddToDashboard,
  isLoggedIn,
  fields,
  sourceId,
  tableName,
}: SummarizeTabProps) {
  const [mode, setMode] = useState<SummarizeMode>('ai');
  const [nlQuery, setNlQuery] = useState('');
  const [recentQueries, setRecentQueries] = useState<string[]>([]);
  const [recentOpen, setRecentOpen] = useState(false);
  const [planOpen, setPlanOpen] = useState(false);
  const [loadStep, setLoadStep] = useState(0);

  // Advance the simulated NL progress steps while the request is in flight
  useEffect(() => {
    if (!nlIsLoading) return;
    setLoadStep(0);
    const t1 = setTimeout(() => setLoadStep(1), 2200);
    const t2 = setTimeout(() => setLoadStep(2), 5500);
    return () => {
      clearTimeout(t1);
      clearTimeout(t2);
    };
  }, [nlIsLoading]);

  // Derive active result set based on current mode
  const activeRows = mode === 'ai' ? nlRows : summarizeRows;
  const activeHeaders = useMemo(
    () => (mode === 'ai' ? (nlRows.length > 0 ? Object.keys(nlRows[0]) : []) : summarizeHeaders),
    [mode, nlRows, summarizeHeaders],
  );

  // Tell the viz layer which result columns are dimensions (group-bys) and which
  // are temporal, straight from the query — so a numeric-valued dimension (e.g.
  // DateTrunc/Year → 2024) isn't mistaken for a measure. Output column name is
  // alias ?? fieldName for both modes.
  const { groupHints, temporalHints } = useMemo(() => {
    if (mode === 'ai') {
      const vb = nlPlan?.viewBy ?? [];
      return {
        groupHints: vb.map((g) => g.alias || g.fieldName),
        temporalHints: vb.filter((g) => g.type === 'DateTrunc').map((g) => g.alias || g.fieldName),
      };
    }
    const isDateTrunc = (e: ViewByEntry) =>
      e.type === 'Transform' &&
      e.transformId != null &&
      fieldTransforms.find((t) => t.id === e.transformId)?.type === 'DateTrunc';
    return {
      groupHints: viewByEntries.map((e) => e.alias || e.fieldName),
      temporalHints: viewByEntries.filter(isDateTrunc).map((e) => e.alias || e.fieldName),
    };
  }, [mode, nlPlan, viewByEntries, fieldTransforms]);

  // The re-runnable plan behind the current result, for "Add to dashboard". AI:
  // the returned plan + the page's source context. Manual: the exact last run.
  const currentPlan = useMemo<SummarizeRequest | null>(() => {
    if (mode === 'ai') {
      if (!nlPlan) return null;
      return {
        context: sourceId ? { sourceId, tableName } : undefined,
        viewBy: nlPlan.viewBy,
        measures: nlPlan.measures,
        filters: nlPlan.filters,
      };
    }
    return summarizeLastRequest;
  }, [mode, nlPlan, summarizeLastRequest, sourceId, tableName]);

  const activeError = mode === 'ai' ? nlError : error;
  // True once an NL query has completed (success with any row count, including 0)
  const nlQueryRan = mode === 'ai' && nlInterpretation != null;
  const showResultsArea = activeHeaders.length > 0 || nlQueryRan;

  // Ask AI requires login. When logged out, the Generate button sends the user to sign in
  // instead of firing a request that would 401.
  const handleGenerate = () => {
    if (!isLoggedIn) {
      window.location.href = `/api/account/login/Google?ReturnUrl=${encodeURIComponent(
        window.location.pathname,
      )}`;
      return;
    }
    const q = nlQuery.trim();
    if (q) {
      setRecentQueries((prev) => [q, ...prev.filter((p) => p !== q)].slice(0, 5));
      setPlanOpen(false); // collapse the raw-plan panel for the incoming result
      onRunNlSummarize(q);
    }
  };

  // Clickable example questions — shown both in the input panel and in the "no data" guidance,
  // so a user whose question matched nothing can fall back to a known-good query.
  const suggestionChips = (
    <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.75, mt: 1 }}>
      {NL_EXAMPLES.map((ex) => (
        <Chip
          key={ex}
          label={ex}
          size="small"
          variant="outlined"
          clickable
          onClick={() => setNlQuery(ex)}
        />
      ))}
    </Box>
  );

  return (
    <>
      {mode === 'manual' && (
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          <strong>SummarizeAsync</strong> — 依 GroupBy 分組並計算 Measures（聚合），可搭配選用的
          Filters。
        </Typography>
      )}

      {/* ── Mode toggle ────────────────────────────────────────────────────── */}
      <ToggleButtonGroup
        size="small"
        exclusive
        value={mode}
        onChange={(_e, v) => {
          if (v !== null) setMode(v);
        }}
        aria-label="summarize mode"
        sx={{ mb: 2 }}
      >
        <ToggleButton value="manual" aria-label="manual mode">
          <SettingsIcon fontSize="small" sx={{ mr: 0.5 }} />
          手動
        </ToggleButton>
        <ToggleButton value="ai" aria-label="ask AI">
          <AutoAwesomeIcon fontSize="small" sx={{ mr: 0.5 }} />問 AI
        </ToggleButton>
      </ToggleButtonGroup>

      {/* ── Manual mode ────────────────────────────────────────────────────── */}
      {mode === 'manual' && (
        <Paper variant="outlined" sx={{ p: 2, mb: 3 }}>
          <FieldTransformEditor
            transforms={fieldTransforms}
            onAdd={onTransformAdd}
            onUpdate={onTransformUpdate}
            onRemove={onTransformRemove}
            fields={fields}
            sourceId={sourceId}
            tableName={tableName}
          />

          <Divider sx={{ my: 2 }} />

          <ViewByEditor
            entries={viewByEntries}
            fields={fields}
            transforms={fieldTransforms}
            onAdd={onViewByAdd}
            onUpdate={onViewByUpdate}
            onRemove={onViewByRemove}
          />

          <Divider sx={{ my: 2 }} />

          <MeasureEditor
            measures={measures}
            fields={fields}
            onAdd={onMeasureAdd}
            onRemove={onMeasureRemove}
            onUpdate={onMeasureUpdate}
          />

          <Divider sx={{ my: 2 }} />

          <FilterEditor
            filters={summarizeFilters}
            fields={fields}
            transforms={fieldTransforms}
            onChange={onSummarizeFiltersChange}
            sourceId={sourceId}
            tableName={tableName}
          />

          <Button
            variant="contained"
            startIcon={<PlayArrowIcon />}
            onClick={onRunSummarize}
            disabled={isLoading || viewByEntries.length === 0 || measures.length === 0}
            sx={{ mt: 2 }}
          >
            {isLoading ? '執行中…' : '執行查詢'}
          </Button>
        </Paper>
      )}

      {/* ── AI mode ────────────────────────────────────────────────────────── */}
      {mode === 'ai' && (
        <Paper
          variant="outlined"
          sx={{
            p: 2.5,
            mb: 3,
            borderColor: 'rgba(94, 106, 210, 0.35)',
            backgroundColor: 'rgba(94, 106, 210, 0.05)',
          }}
        >
          <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
            用一般文字描述你想看的內容，AI 會把你的需求轉換成查詢並回傳結果。
          </Typography>

          <TextField
            multiline
            minRows={2}
            maxRows={5}
            fullWidth
            placeholder="例如：「EMEA 地區各產品類別的營收」"
            value={nlQuery}
            onChange={(e) => setNlQuery(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && (e.metaKey || e.ctrlKey) && nlQuery.trim()) {
                handleGenerate();
              }
            }}
            sx={{ mb: 1.5 }}
          />

          <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.75, mb: 2 }}>
            {NL_EXAMPLES.map((ex) => (
              <Chip
                key={ex}
                label={ex}
                size="small"
                variant="outlined"
                onClick={() => setNlQuery(ex)}
                clickable
              />
            ))}
          </Box>

          {recentQueries.length > 0 && (
            <Box sx={{ mb: 2 }}>
              {/* Collapsed by default so recents don't blend into the examples above */}
              <Button
                size="small"
                startIcon={<HistoryIcon sx={{ fontSize: 16 }} />}
                endIcon={
                  recentOpen ? (
                    <ExpandLessIcon sx={{ fontSize: 16 }} />
                  ) : (
                    <ExpandMoreIcon sx={{ fontSize: 16 }} />
                  )
                }
                onClick={() => setRecentOpen((o) => !o)}
                sx={{ color: 'text.secondary', px: 1, minWidth: 0 }}
              >
                最近查詢 ({recentQueries.length})
              </Button>
              <Collapse in={recentOpen}>
                <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.75, mt: 1, pl: 0.5 }}>
                  {recentQueries.map((q) => (
                    <Chip
                      key={q}
                      label={q}
                      size="small"
                      onClick={() => setNlQuery(q)}
                      clickable
                      sx={{ maxWidth: 360 }}
                    />
                  ))}
                </Box>
              </Collapse>
            </Box>
          )}

          <Button
            variant="contained"
            startIcon={isLoggedIn ? <AutoAwesomeIcon /> : <LoginIcon />}
            onClick={handleGenerate}
            disabled={isLoggedIn && (nlIsLoading || !nlQuery.trim())}
          >
            {!isLoggedIn ? '登入以使用 AI' : nlIsLoading ? '生成中…' : '生成'}
          </Button>
        </Paper>
      )}

      {/* ── Staged AI progress ─────────────────────────────────────────────── */}
      {mode === 'ai' && nlIsLoading && (
        <Paper variant="outlined" sx={{ p: 2, mb: 2 }}>
          {NL_STEPS.map((step, i) => (
            <Box key={step} sx={{ display: 'flex', alignItems: 'center', gap: 1.25, py: 0.5 }}>
              {i < loadStep ? (
                <CheckIcon sx={{ fontSize: 16, color: 'success.main' }} />
              ) : i === loadStep ? (
                <CircularProgress size={14} thickness={5} />
              ) : (
                <Box sx={{ width: 16, display: 'flex', justifyContent: 'center' }}>
                  <Box
                    sx={{ width: 5, height: 5, borderRadius: '50%', bgcolor: 'text.disabled' }}
                  />
                </Box>
              )}
              <Typography
                variant="body2"
                sx={{ color: i <= loadStep ? 'text.primary' : 'text.disabled' }}
              >
                {step}
              </Typography>
            </Box>
          ))}
        </Paper>
      )}

      {activeError && mode === 'manual' && (
        <Alert severity="error" sx={{ mb: 2 }}>
          查詢失敗，詳情請查看主控台。
        </Alert>
      )}

      {activeError && mode === 'ai' && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          <Typography variant="body2">
            AI 無法回答這個問題——這份資料可能沒有符合的欄位或數值。試試看以下範例：
          </Typography>
          {suggestionChips}
        </Alert>
      )}

      {showResultsArea && (
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
                回傳 {activeRows.length} 筆資料
              </Typography>
              {mode === 'ai' && nlInterpretation && (
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.25 }}>
                  <Typography variant="caption" color="text.secondary" sx={{ fontStyle: 'italic' }}>
                    {nlInterpretation}
                  </Typography>
                  {nlPlan && (
                    <ActionTooltip title="檢視原始查詢計畫">
                      <IconButton
                        size="small"
                        onClick={() => setPlanOpen((o) => !o)}
                        sx={{ p: 0.25 }}
                      >
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
          {activeHeaders.length === 0 && (
            <Alert severity="warning" sx={{ mb: 2 }}>
              <Typography variant="body2">
                找不到符合此問題的資料——你詢問的日期或數值可能不存在於此資料中。試試看以下範例：
              </Typography>
              {suggestionChips}
            </Alert>
          )}

          {/* ── Visualization: switch between any viz whose constraints fit the result ── */}
          {activeHeaders.length > 0 && (
            <VizSwitcher
              key={`${mode}-${mode === 'ai' ? nlResultVersion : summarizeVersion}`}
              rows={activeRows}
              headers={activeHeaders}
              groupHints={groupHints}
              temporalHints={temporalHints}
              currentPlan={currentPlan}
              canAddToDashboard={canAddToDashboard}
            />
          )}
        </>
      )}
    </>
  );
}
