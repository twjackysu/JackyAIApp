import AccountTreeOutlinedIcon from '@mui/icons-material/AccountTreeOutlined';
import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import TableRowsOutlinedIcon from '@mui/icons-material/TableRowsOutlined';
import WarningAmberIcon from '@mui/icons-material/WarningAmber';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import { ThemeProvider } from '@mui/material/styles';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import { useGetUserInfoQuery } from '../apis/accountApis';
import {
  useGetMaxDateQuery,
  useGetRowCountQuery,
  useGetSchemaQuery,
  useNlSummarizeMutation,
} from '../apis/dataAnalysisApis';
import { DataAnalysisNav } from '../components/DataAnalysisNav';

import { DetailsTab } from './components/DetailsTab';
import { OverviewStrip } from './components/OverviewStrip';
import { SchemaTab } from './components/SchemaTab';
import { SourceManagerDialog } from './components/SourceManagerDialog';
import { SourcePanel } from './components/SourcePanel';
import { SummarizeTab } from './components/SummarizeTab';
import { TabPanel } from './components/TabPanel';
import { UploadDataDialog } from './components/UploadDataDialog';
import { DATE_CANONICAL_TYPES, DEFAULT_TABLE } from './constants';
import { useDateField } from './hooks/useDateField';
import { useDetails } from './hooks/useDetails';
import { useSourceContext } from './hooks/useSourceContext';
import { useSummarize } from './hooks/useSummarize';
import { dataAnalysisTheme } from './theme';
import { makeViewByEntry } from './utils/groupField';

import type { NlSummarizeResult } from '../apis/dataAnalysisApis/types';

import { fmtNum } from '@/utils/format';

export default function DataAnalysis() {
  const [tab, setTab] = useState(1);

  // ── Auth ──────────────────────────────────────────────────────────────────
  const { data: userInfoRes } = useGetUserInfoQuery();
  const isLoggedIn = !!userInfoRes?.data;

  // ── Source context ────────────────────────────────────────────────────────
  const {
    activeSourceId,
    setActiveSourceId,
    activeTableName,
    setActiveTableName,
    tableNameInput,
    setTableNameInput,
    managerOpen,
    setManagerOpen,
    sources,
    activeSource,
    ctxSourceId,
    ctxTableName,
    applyTableName,
    sessionInfo,
    setSessionInfo,
    clearSession,
    uploadOpen,
    setUploadOpen,
  } = useSourceContext(isLoggedIn);

  const sessionId = sessionInfo?.sessionId;

  // ── Schema ────────────────────────────────────────────────────────────────
  const {
    data: schemaData,
    isLoading: schemaLoading,
    error: schemaError,
  } = useGetSchemaQuery(
    { sourceId: ctxSourceId, tableName: ctxTableName },
    { skip: !!sessionId }, // skip regular schema call when in session mode
  );

  // When in session mode, use the schema that came back with the upload response
  const fields = sessionId ? (sessionInfo?.schema ?? []) : (schemaData?.data?.fields ?? []);

  // ── Date field ────────────────────────────────────────────────────────────
  const { dateField, setDateField } = useDateField(fields);

  // ── Summarize ─────────────────────────────────────────────────────────────
  const summarize = useSummarize({ ctxSourceId, ctxTableName, fields, sessionId });

  // ── NL Summarize ──────────────────────────────────────────────────────────
  const [nlSummarize, { isLoading: nlIsLoading, error: nlError }] = useNlSummarizeMutation();
  const [nlResult, setNlResult] = useState<NlSummarizeResult | null>(null);
  // Bumped when a new AI result arrives, so the result view can be remounted
  // (keyed) to reset its per-result UI state.
  const [nlResultVersion, setNlResultVersion] = useState(0);

  const handleRunNlSummarize = async (query: string) => {
    setNlResult(null);
    const res = await nlSummarize({
      context: ctxSourceId ? { sourceId: ctxSourceId, tableName: ctxTableName } : undefined,
      sessionId,
      query,
    });
    if ('data' in res && res.data?.data) {
      setNlResult(res.data.data);
      setNlResultVersion((v) => v + 1);
    }
  };

  // ── Details ───────────────────────────────────────────────────────────────
  // Row counts group by the date field's year, so they need it to be a real date column.
  const rowCountField = fields.some(
    (f) => f.name === dateField && DATE_CANONICAL_TYPES.includes(f.canonicalType),
  )
    ? dateField
    : undefined;
  const details = useDetails({
    ctxSourceId,
    ctxTableName,
    tab,
    dateField,
    sessionId,
  });

  // ── Max Date ──────────────────────────────────────────────────────────────
  const { data: maxDateData, isLoading: maxDateLoading } = useGetMaxDateQuery({
    sessionId,
    sourceId: ctxSourceId,
    tableName: ctxTableName,
    dateField,
  });
  const maxDate = maxDateData?.data?.maxDate;

  // ── Row count (for the overview strip) ────────────────────────────────────
  // Session uploads already know their row count. DB sources get an exact count from one
  // aggregate query; the same query (no filters) backs the Details tab's total, so it's cached.
  const { data: dbRowCount, isFetching: rowCountLoading } = useGetRowCountQuery(
    { sourceId: ctxSourceId, tableName: ctxTableName, dateField: rowCountField ?? '', filters: [] },
    { skip: !!sessionId || !rowCountField },
  );
  const rowCountDisplay = sessionId
    ? sessionInfo && fmtNum(sessionInfo.rowCount)
    : dbRowCount == null
      ? undefined
      : fmtNum(dbRowCount);

  // ── Source change: reset all query config ─────────────────────────────────
  const handleSourceChange = (id: string) => {
    const newId = id === '' ? null : id;
    setActiveSourceId(newId);
    if (newId === null) {
      setTableNameInput(DEFAULT_TABLE);
      setActiveTableName(DEFAULT_TABLE);
    } else {
      const src = sources.find((s) => s.id === newId);
      const tbl = src?.defaultTableName ?? '';
      setTableNameInput(tbl);
      setActiveTableName(tbl);
    }
    summarize.reset();
    details.reset();
    setDateField('OrderDate');
    setNlResult(null);
  };

  const handleClearSession = () => {
    clearSession();
    summarize.reset();
    details.reset();
    setNlResult(null);
  };

  // ── Render ────────────────────────────────────────────────────────────────
  return (
    <ThemeProvider theme={dataAnalysisTheme}>
      <Box>
        <DataAnalysisNav />
        <Box sx={{ mb: 3 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25 }}>
            <Typography variant="h5" sx={{ color: 'text.primary' }}>
              資料分析 Library
            </Typography>
            <Box
              component="span"
              sx={{
                fontSize: '0.7rem',
                fontWeight: 500,
                letterSpacing: '0.02em',
                color: 'text.secondary',
                border: '1px solid',
                borderColor: 'divider',
                borderRadius: '6px',
                px: 0.75,
                py: 0.25,
                lineHeight: 1.4,
              }}
            >
              Live Demo
            </Box>
          </Box>
          <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
            探索資料結構（Schema）、執行 AI 摘要分析，並深入查看明細資料。
          </Typography>
        </Box>

        <SourcePanel
          isLoggedIn={isLoggedIn}
          sources={sources}
          activeSourceId={activeSourceId}
          tableNameInput={tableNameInput}
          sessionInfo={sessionInfo ?? null}
          onSourceChange={handleSourceChange}
          onTableNameChange={setTableNameInput}
          onApplyTableName={applyTableName}
          onOpenManager={() => setManagerOpen(true)}
          onOpenUpload={() => setUploadOpen(true)}
          onClearSession={handleClearSession}
        />

        {!sessionId && activeSourceId && (
          <Alert severity="info" icon={<WarningAmberIcon />} sx={{ mb: 2 }}>
            <strong>已選擇自訂資料來源</strong> — 正在查詢{' '}
            <em>{activeSource?.name ?? activeSourceId}</em>。連線資訊已加密儲存並透過 HTTPS
            傳輸。注意：若此伺服器無法從公開網路連線，連接外部資料庫可能會失敗。
          </Alert>
        )}

        {!sessionId && (
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            {activeSourceId ? (
              <>
                查詢中 <strong>{activeSource?.name}</strong>
                {activeTableName && (
                  <>
                    {' → '}
                    <Box
                      component="code"
                      sx={{
                        bgcolor: 'action.selected',
                        px: 0.5,
                        borderRadius: '3px',
                        fontFamily: 'monospace',
                        fontSize: '0.875em',
                      }}
                    >
                      {activeTableName}
                    </Box>
                  </>
                )}
              </>
            ) : (
              <>
                查詢中{' '}
                <Box
                  component="code"
                  sx={{
                    bgcolor: 'action.selected',
                    px: 0.5,
                    borderRadius: '3px',
                    fontFamily: 'monospace',
                    fontSize: '0.875em',
                  }}
                >
                  {activeTableName || DEFAULT_TABLE}
                </Box>
              </>
            )}
          </Typography>
        )}

        <OverviewStrip
          rowCountDisplay={rowCountDisplay}
          rowCountLoading={!sessionId && rowCountLoading}
          columnCount={fields.length}
          schemaLoading={!sessionId && schemaLoading}
          dateField={dateField}
          maxDate={maxDate}
          maxDateLoading={maxDateLoading}
        />

        <Box
          sx={{
            display: 'inline-flex',
            bgcolor: 'rgba(255, 255, 255, 0.03)',
            borderRadius: '10px',
            px: 0.5,
            border: '1px solid',
            borderColor: 'divider',
          }}
        >
          <Tabs value={tab} onChange={(_, v: number) => setTab(v)}>
            <Tab
              icon={<AccountTreeOutlinedIcon fontSize="small" />}
              iconPosition="start"
              label="Schema"
            />
            <Tab
              icon={<AutoAwesomeIcon fontSize="small" />}
              iconPosition="start"
              label="摘要分析"
            />
            <Tab
              icon={<TableRowsOutlinedIcon fontSize="small" />}
              iconPosition="start"
              label="明細資料"
            />
          </Tabs>
        </Box>

        <TabPanel value={tab} index={0}>
          <SchemaTab
            fields={fields}
            isLoading={!sessionId && schemaLoading}
            error={!sessionId ? schemaError : undefined}
            dateField={dateField}
            onDateFieldChange={setDateField}
          />
        </TabPanel>

        <TabPanel value={tab} index={1}>
          <SummarizeTab
            fieldTransforms={summarize.fieldTransforms}
            onTransformAdd={summarize.addTransform}
            onTransformUpdate={summarize.updateTransform}
            onTransformRemove={summarize.removeTransform}
            viewByEntries={summarize.viewByEntries}
            onViewByAdd={() =>
              summarize.setViewByEntries((prev) => [
                ...prev,
                makeViewByEntry(fields[0]?.name ?? ''),
              ])
            }
            onViewByUpdate={(entry) =>
              summarize.setViewByEntries((prev) => prev.map((e) => (e.id === entry.id ? entry : e)))
            }
            onViewByRemove={(id) =>
              summarize.setViewByEntries((prev) => prev.filter((e) => e.id !== id))
            }
            measures={summarize.measures}
            onMeasureAdd={summarize.addMeasure}
            onMeasureRemove={summarize.removeMeasure}
            onMeasureUpdate={summarize.updateMeasure}
            summarizeFilters={summarize.summarizeFilters}
            onSummarizeFiltersChange={summarize.setSummarizeFilters}
            onRunSummarize={summarize.handleRunSummarize}
            isLoading={summarize.summarizeLoading}
            error={summarize.summarizeError}
            summarizeRows={summarize.summarizeRows}
            summarizeHeaders={summarize.summarizeHeaders}
            onRunNlSummarize={handleRunNlSummarize}
            nlIsLoading={nlIsLoading}
            nlError={nlError}
            nlRows={nlResult?.rows ?? []}
            nlInterpretation={nlResult?.interpretation}
            nlPlan={nlResult?.plan}
            nlNotes={nlResult?.notes}
            nlResultVersion={nlResultVersion}
            summarizeVersion={summarize.resultVersion}
            summarizeLastRequest={summarize.lastRequest}
            canAddToDashboard={isLoggedIn && !sessionId}
            isLoggedIn={isLoggedIn}
            fields={fields}
            sourceId={ctxSourceId}
            tableName={ctxTableName}
          />
        </TabPanel>

        <TabPanel value={tab} index={2}>
          <DetailsTab
            fields={fields}
            detailsFilters={details.detailsFilters}
            onFiltersChange={details.handleDetailsFiltersChange}
            detailsLoading={details.detailsLoading}
            detailsError={details.detailsError}
            detailsRows={details.detailsRows}
            detailsHeaders={details.detailsHeaders}
            detailsTotalCount={details.detailsTotalCount}
            page={details.page}
            onPageChange={details.setPage}
            sourceId={ctxSourceId}
            tableName={ctxTableName}
          />
        </TabPanel>

        <SourceManagerDialog open={managerOpen} onClose={() => setManagerOpen(false)} />

        <UploadDataDialog
          open={uploadOpen}
          onClose={() => setUploadOpen(false)}
          onSessionCreated={(info) => {
            setSessionInfo(info);
            setUploadOpen(false);
            summarize.reset();
            details.reset();
          }}
        />
      </Box>
    </ThemeProvider>
  );
}
