import Box from '@mui/material/Box';
import { ThemeProvider } from '@mui/material/styles';
import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';

import { useGetUserInfoQuery } from '../apis/accountApis';
import { useGetSchemaQuery } from '../apis/dataAnalysisApis';
import { DataAnalysisNav } from '../components/DataAnalysisNav';

import { dataAnalysisAiChatFor } from './aiChatConfig';
import { AnalysisTabs } from './components/AnalysisTabs';
import { DetailsTab } from './components/DetailsTab';
import { OverviewStrip } from './components/OverviewStrip';
import { PageHeader } from './components/PageHeader';
import { QueryTarget } from './components/QueryTarget';
import { SchemaTab } from './components/SchemaTab';
import { SourceManagerDialog } from './components/SourceManagerDialog';
import { SourcePanel } from './components/SourcePanel';
import { SummarizeTab } from './components/SummarizeTab';
import { TabPanel } from './components/TabPanel';
import { UploadDataDialog } from './components/UploadDataDialog';
import { useDateField } from './hooks/useDateField';
import { useDetails } from './hooks/useDetails';
import { useNlSummarize } from './hooks/useNlSummarize';
import { useOverviewFacts } from './hooks/useOverviewFacts';
import { useSourceContext } from './hooks/useSourceContext';
import { useSummarize } from './hooks/useSummarize';
import { dataAnalysisTheme } from './theme';
import { dataAnalysisPageContext } from './utils/aiPageContext';
import { makeViewByEntry } from './utils/groupField';
import { tableForSource } from './utils/pageRules';

import { AiAssistantLauncher } from '@/components/aiChat/AiAssistantLauncher';

export default function DataAnalysis() {
  const [tab, setTab] = useState(1);

  // ── Auth ──────────────────────────────────────────────────────────────────
  const { data: userInfoRes, isLoading: isAuthLoading } = useGetUserInfoQuery();
  const isLoggedIn = !!userInfoRes?.data;
  // An answer that saved a chart and was asked to open it takes the reader to that dashboard.
  const navigate = useNavigate();

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

  // The assistant reads the data on screen: the page tells it which with every message.
  const aiChat = useMemo(
    () => ({
      ...dataAnalysisAiChatFor(!!sessionId),
      pageContext: dataAnalysisPageContext({
        sourceId: ctxSourceId,
        tableName: ctxTableName,
        sessionId,
      }),
    }),
    [ctxSourceId, ctxTableName, sessionId],
  );

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

  // ── Date field, summarize, AI, details, overview ──────────────────────────
  const { dateField, setDateField } = useDateField(fields);
  const summarize = useSummarize({ ctxSourceId, ctxTableName, fields, sessionId });
  const nl = useNlSummarize({ ctxSourceId, ctxTableName, sessionId });
  const details = useDetails({ ctxSourceId, ctxTableName, tab, dateField, sessionId });
  const overview = useOverviewFacts({ ctxSourceId, ctxTableName, fields, dateField, sessionInfo });

  // ── Source change: reset all query config ─────────────────────────────────
  const handleSourceChange = (id: string) => {
    const newId = id === '' ? null : id;
    setActiveSourceId(newId);
    const table = tableForSource(sources, newId);
    setTableNameInput(table);
    setActiveTableName(table);
    summarize.reset();
    details.reset();
    setDateField('OrderDate');
    nl.clear();
  };

  const handleClearSession = () => {
    clearSession();
    summarize.reset();
    details.reset();
    nl.clear();
  };

  // ── Render ────────────────────────────────────────────────────────────────
  return (
    <ThemeProvider theme={dataAnalysisTheme}>
      <Box>
        <DataAnalysisNav />
        <PageHeader />
        {/* AI assistant: a one-line box until it is used, then the whole conversation (as on /finance) */}
        <AiAssistantLauncher
          config={aiChat}
          isAuthenticated={isLoggedIn}
          isAuthLoading={isAuthLoading}
          onNavigate={navigate}
        />

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

        {!sessionId && (
          <QueryTarget
            activeSourceId={activeSourceId}
            activeSourceName={activeSource?.name}
            activeTableName={activeTableName}
          />
        )}

        <OverviewStrip
          rowCountDisplay={overview.rowCountDisplay}
          rowCountLoading={overview.rowCountLoading}
          columnCount={fields.length}
          schemaLoading={!sessionId && schemaLoading}
          dateField={dateField}
          maxDate={overview.maxDate}
          maxDateLoading={overview.maxDateLoading}
        />

        <AnalysisTabs tab={tab} onChange={setTab} />

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
            onRunNlSummarize={nl.run}
            nlIsLoading={nl.isLoading}
            nlError={nl.error}
            nlRows={nl.result?.rows ?? []}
            nlInterpretation={nl.result?.interpretation}
            nlPlan={nl.result?.plan}
            nlNotes={nl.result?.notes}
            nlResultVersion={nl.version}
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
            onExport={details.exportCsv}
            exporting={details.exporting}
            exportResult={details.exportResult}
            exportError={details.exportError}
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
