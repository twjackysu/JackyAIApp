import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import SettingsIcon from '@mui/icons-material/Settings';
import Alert from '@mui/material/Alert';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';
import Typography from '@mui/material/Typography';
import { useMemo, useState } from 'react';

import { AskAiPanel } from './AskAiPanel';
import { ExampleChips } from './ExampleChips';
import { ManualPanel } from './ManualPanel';
import { NlProgress } from './NlProgress';
import { dashboardPlan, rememberQuery, resultColumnHints, SummarizeMode } from './summarizeResult';
import { SummarizeResults } from './SummarizeResults';
import { useNlProgressStep } from './useNlProgressStep';

import type { SummarizeTabProps } from './types';

export function SummarizeTab(props: SummarizeTabProps) {
  const {
    fieldTransforms,
    viewByEntries,
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
    sourceId,
    tableName,
  } = props;
  const [mode, setMode] = useState<SummarizeMode>('ai');
  const [nlQuery, setNlQuery] = useState('');
  const [recentQueries, setRecentQueries] = useState<string[]>([]);
  const [planOpen, setPlanOpen] = useState(false);
  const loadStep = useNlProgressStep(nlIsLoading);

  // Derive active result set based on current mode
  const activeRows = mode === 'ai' ? nlRows : summarizeRows;
  const activeHeaders = useMemo(
    () => (mode === 'ai' ? (nlRows.length > 0 ? Object.keys(nlRows[0]) : []) : summarizeHeaders),
    [mode, nlRows, summarizeHeaders],
  );
  const hints = useMemo(
    () => resultColumnHints(mode, nlPlan, viewByEntries, fieldTransforms),
    [mode, nlPlan, viewByEntries, fieldTransforms],
  );
  const currentPlan = useMemo(
    () => dashboardPlan(mode, nlPlan, summarizeLastRequest, sourceId, tableName),
    [mode, nlPlan, summarizeLastRequest, sourceId, tableName],
  );

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
      setRecentQueries((prev) => rememberQuery(prev, q));
      setPlanOpen(false); // collapse the raw-plan panel for the incoming result
      onRunNlSummarize(q);
    }
  };

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

      {mode === 'manual' && <ManualPanel {...props} />}

      {mode === 'ai' && (
        <AskAiPanel
          query={nlQuery}
          onQueryChange={setNlQuery}
          recentQueries={recentQueries}
          isLoggedIn={isLoggedIn}
          nlIsLoading={nlIsLoading}
          onGenerate={handleGenerate}
        />
      )}

      {mode === 'ai' && nlIsLoading && <NlProgress step={loadStep} />}

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
          <ExampleChips onPick={setNlQuery} spacing={{ mt: 1 }} />
        </Alert>
      )}

      {showResultsArea && (
        <SummarizeResults
          mode={mode}
          rows={activeRows}
          headers={activeHeaders}
          nlInterpretation={nlInterpretation}
          nlPlan={nlPlan}
          nlNotes={nlNotes}
          planOpen={planOpen}
          onTogglePlan={() => setPlanOpen((o) => !o)}
          onPickExample={setNlQuery}
          resultKey={`${mode}-${mode === 'ai' ? nlResultVersion : summarizeVersion}`}
          groupHints={hints.groupHints}
          temporalHints={hints.temporalHints}
          currentPlan={currentPlan}
          canAddToDashboard={canAddToDashboard}
        />
      )}
    </>
  );
}
