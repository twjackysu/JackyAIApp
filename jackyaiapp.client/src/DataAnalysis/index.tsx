import WarningAmberIcon from '@mui/icons-material/WarningAmber';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import { useGetUserInfoQuery } from '../apis/accountApis';
import { useGetMaxDateQuery, useGetSchemaQuery } from '../apis/dataAnalysisApis';

import { DEFAULT_TABLE } from './constants';
import { useDetails } from './hooks/useDetails';
import { useDateField } from './hooks/useDateField';
import { useSourceContext } from './hooks/useSourceContext';
import { useSummarize } from './hooks/useSummarize';
import { makeViewByEntry } from './utils/groupField';

import { DetailsTab } from './components/DetailsTab';
import { MaxDateTab } from './components/MaxDateTab';
import { SchemaTab } from './components/SchemaTab';
import { SourceManagerDialog } from './components/SourceManagerDialog';
import { SourcePanel } from './components/SourcePanel';
import { SummarizeTab } from './components/SummarizeTab';
import { TabPanel } from './components/TabPanel';

export default function DataAnalysis() {
  const [tab, setTab] = useState(0);

  // ── Auth ──────────────────────────────────────────────────────────────────
  const { data: userInfoRes } = useGetUserInfoQuery();
  const isLoggedIn = !!userInfoRes?.data;

  // ── Source context ────────────────────────────────────────────────────────
  const {
    activeSourceId, setActiveSourceId,
    activeTableName, setActiveTableName,
    tableNameInput, setTableNameInput,
    managerOpen, setManagerOpen,
    sources, activeSource,
    ctxSourceId, ctxTableName,
    applyTableName,
  } = useSourceContext(isLoggedIn);

  // ── Schema ────────────────────────────────────────────────────────────────
  const { data: schemaData, isLoading: schemaLoading, error: schemaError } =
    useGetSchemaQuery({ sourceId: ctxSourceId, tableName: ctxTableName });
  const fields = schemaData?.data?.fields ?? [];

  // ── Date field ────────────────────────────────────────────────────────────
  const { dateField, setDateField } = useDateField(fields);

  // ── Summarize ─────────────────────────────────────────────────────────────
  const summarize = useSummarize({ ctxSourceId, ctxTableName, fields });

  // ── Details ───────────────────────────────────────────────────────────────
  const details = useDetails({ ctxSourceId, ctxTableName, tab, dateField });

  // ── Max Date ──────────────────────────────────────────────────────────────
  const { data: maxDateData, isLoading: maxDateLoading } =
    useGetMaxDateQuery({ sourceId: ctxSourceId, tableName: ctxTableName, dateField });
  const maxDate = maxDateData?.data?.maxDate;

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
  };

  // ── Render ────────────────────────────────────────────────────────────────
  return (
    <Box>
      <Typography variant="h5" gutterBottom>
        DataAnalysis Library — Live Demo
      </Typography>

      <SourcePanel
        isLoggedIn={isLoggedIn}
        sources={sources}
        activeSourceId={activeSourceId}
        tableNameInput={tableNameInput}
        onSourceChange={handleSourceChange}
        onTableNameChange={setTableNameInput}
        onApplyTableName={applyTableName}
        onOpenManager={() => setManagerOpen(true)}
      />

      {activeSourceId && (
        <Alert severity="info" icon={<WarningAmberIcon />} sx={{ mb: 2 }}>
          <strong>Custom data source selected</strong> — querying{' '}
          <em>{activeSource?.name ?? activeSourceId}</em>.
          {' '}Credentials are encrypted and stored securely, and transmitted over HTTPS.
          Note: if this server is not reachable from the public internet, connections to
          external databases may fail.
        </Alert>
      )}

      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        {activeSourceId ? (
          <>
            Querying <strong>{activeSource?.name}</strong>
            {activeTableName && (
              <>
                {' → '}
                <Box
                  component="code"
                  sx={{ bgcolor: 'action.selected', px: 0.5, borderRadius: '3px', fontFamily: 'monospace', fontSize: '0.875em' }}
                >
                  {activeTableName}
                </Box>
              </>
            )}
          </>
        ) : (
          <>
            All 4 query types demonstrated against{' '}
            <Box
              component="code"
              sx={{ bgcolor: 'action.selected', px: 0.5, borderRadius: '3px', fontFamily: 'monospace', fontSize: '0.875em' }}
            >
              {activeTableName || DEFAULT_TABLE}
            </Box>
          </>
        )}
      </Typography>

      <Box sx={{ borderBottom: 1, borderColor: 'divider' }}>
        <Tabs value={tab} onChange={(_, v: number) => setTab(v)}>
          <Tab label="① Schema" />
          <Tab label="② Summarize" />
          <Tab label="③ Details" />
          <Tab label="④ Max Date" />
        </Tabs>
      </Box>

      <TabPanel value={tab} index={0}>
        <SchemaTab
          fields={fields}
          isLoading={schemaLoading}
          error={schemaError}
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
            summarize.setViewByEntries((prev) => [...prev, makeViewByEntry(fields[0]?.name ?? '')])
          }
          onViewByUpdate={(entry) =>
            summarize.setViewByEntries((prev) =>
              prev.map((e) => (e.id === entry.id ? entry : e)),
            )
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

      <TabPanel value={tab} index={3}>
        <MaxDateTab
          dateField={dateField}
          activeTableName={activeTableName}
          defaultTable={DEFAULT_TABLE}
          maxDate={maxDate}
          isLoading={maxDateLoading}
        />
      </TabPanel>

      <SourceManagerDialog open={managerOpen} onClose={() => setManagerOpen(false)} />
    </Box>
  );
}
