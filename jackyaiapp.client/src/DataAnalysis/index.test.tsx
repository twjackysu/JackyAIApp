import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import DataAnalysis from './index';

import type { FieldDefinition, SessionInfo } from '../apis/dataAnalysisApis/types';

type Props = Record<string, unknown>;
const mocks = vi.hoisted(() => ({
  user: vi.fn(),
  schema: vi.fn(),
  maxDate: vi.fn(),
  rowCount: vi.fn(),
  nl: vi.fn(),
  nlState: { isLoading: false, error: undefined as unknown },
  source: {} as Record<string, unknown>,
  sourceArgs: vi.fn(),
  summarize: {} as Record<string, unknown>,
  summarizeArgs: vi.fn(),
  details: {} as Record<string, unknown>,
  detailsArgs: vi.fn(),
  props: {} as Record<string, Record<string, unknown>>,
}));

vi.mock('react-router-dom', () => ({ useNavigate: () => vi.fn() }));
vi.mock('../apis/accountApis', () => ({ useGetUserInfoQuery: () => mocks.user() }));
vi.mock('../apis/dataAnalysisApis', () => ({
  useGetSchemaQuery: (...a: unknown[]) => mocks.schema(...a),
  useGetMaxDateQuery: (...a: unknown[]) => mocks.maxDate(...a),
  useGetRowCountQuery: (...a: unknown[]) => mocks.rowCount(...a),
  useNlSummarizeMutation: () => [mocks.nl, mocks.nlState],
}));
vi.mock('./hooks/useSourceContext', () => ({
  useSourceContext: (isLoggedIn: boolean) => (mocks.sourceArgs(isLoggedIn), mocks.source),
}));
vi.mock('./hooks/useSummarize', () => ({
  useSummarize: (args: unknown) => (mocks.summarizeArgs(args), mocks.summarize),
}));
vi.mock('./hooks/useDetails', () => ({
  useDetails: (args: unknown) => (mocks.detailsArgs(args), mocks.details),
}));
// Children with their own tests only record what the page gives them.
const stub = (name: string) => (p: Props) => {
  mocks.props[name] = p;
  return `[${name}]`;
};
vi.mock('../components/DataAnalysisNav', () => ({ DataAnalysisNav: () => '[Nav]' }));
// The assistant's panel is its own chunk, fetched once the visitor is signed in; as on /finance's
// test, a stand-in, so the fetch does not outlive the test.
vi.mock('@/components/aiChat', () => ({ default: () => '[AiAssistantPanel]' }));
vi.mock('./components/SourcePanel', () => ({ SourcePanel: (p: Props) => stub('SourcePanel')(p) }));
vi.mock('./components/OverviewStrip', () => ({
  OverviewStrip: (p: Props) => stub('OverviewStrip')(p),
}));
vi.mock('./components/SchemaTab', () => ({ SchemaTab: (p: Props) => stub('SchemaTab')(p) }));
vi.mock('./components/SummarizeTab', () => ({
  SummarizeTab: (p: Props) => stub('SummarizeTab')(p),
}));
vi.mock('./components/DetailsTab', () => ({ DetailsTab: (p: Props) => stub('DetailsTab')(p) }));
vi.mock('./components/SourceManagerDialog', () => ({
  SourceManagerDialog: (p: Props) => stub('SourceManagerDialog')(p),
}));
vi.mock('./components/UploadDataDialog', () => ({
  UploadDataDialog: (p: Props) => stub('UploadDataDialog')(p),
}));

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const field = (name: string, canonicalType: FieldDefinition['canonicalType']): FieldDefinition => ({
  name,
  dataType: 'x',
  isNullable: false,
  canonicalType,
});
const ordersFields = [
  field('Region', 'String'),
  field('OrderDate', 'DateTime'),
  field('Revenue', 'Decimal'),
];
const session: SessionInfo = {
  sessionId: 's-1',
  name: 'upload.csv',
  rowCount: 12345,
  expiresAt: '2026-10-25T00:00:00Z',
  schema: [field('Day', 'Date'), field('Amount', 'Decimal')],
};

function sourceContext(overrides: Record<string, unknown> = {}) {
  return {
    activeSourceId: null,
    setActiveSourceId: vi.fn(),
    activeTableName: 'dbo.Orders',
    setActiveTableName: vi.fn(),
    tableNameInput: 'dbo.Orders',
    setTableNameInput: vi.fn(),
    managerOpen: false,
    setManagerOpen: vi.fn(),
    sources: [
      { id: 'src-1', name: '銷售庫', defaultTableName: 'sales.Daily' },
      { id: 'src-2', name: '空表庫' },
    ],
    activeSource: undefined,
    ctxSourceId: undefined,
    ctxTableName: 'dbo.Orders',
    applyTableName: vi.fn(),
    sessionInfo: null,
    setSessionInfo: vi.fn(),
    clearSession: vi.fn(),
    uploadOpen: false,
    setUploadOpen: vi.fn(),
    ...overrides,
  };
}

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  mocks.user.mockReturnValue({ data: { data: { id: 'u1' } } });
  mocks.schema.mockReturnValue({
    data: { data: { fields: ordersFields } },
    isLoading: false,
    error: undefined,
  });
  mocks.maxDate.mockReturnValue({ data: { data: { maxDate: '2026-10-20' } }, isLoading: false });
  mocks.rowCount.mockReturnValue({ data: 98765, isFetching: false });
  mocks.nlState.isLoading = false;
  mocks.nlState.error = undefined;
  mocks.source = sourceContext();
  mocks.summarize = {
    fieldTransforms: [],
    addTransform: vi.fn(),
    updateTransform: vi.fn(),
    removeTransform: vi.fn(),
    viewByEntries: [],
    setViewByEntries: vi.fn(),
    measures: [],
    addMeasure: vi.fn(),
    removeMeasure: vi.fn(),
    updateMeasure: vi.fn(),
    summarizeFilters: [],
    setSummarizeFilters: vi.fn(),
    handleRunSummarize: vi.fn(),
    summarizeLoading: false,
    summarizeError: undefined,
    summarizeRows: [],
    summarizeHeaders: [],
    resultVersion: 3,
    lastRequest: null,
    reset: vi.fn(),
  };
  mocks.details = {
    detailsFilters: [],
    handleDetailsFiltersChange: vi.fn(),
    detailsLoading: false,
    detailsError: undefined,
    detailsRows: [],
    detailsHeaders: [],
    detailsTotalCount: 0,
    page: 0,
    setPage: vi.fn(),
    exportCsv: vi.fn(),
    exporting: false,
    exportResult: null,
    exportError: undefined,
    reset: vi.fn(),
  };
  mocks.props = {};
  container = document.createElement('div');
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  act(() => root.unmount());
  container.remove();
  vi.clearAllMocks();
});

const render = async () => {
  await act(async () => root.render(<DataAnalysis />));
};
const props = (name: string) => mocks.props[name];
const text = () => container.textContent ?? '';
const call = async (fn: unknown, ...args: unknown[]) => {
  await act(async () => {
    await (fn as (...a: unknown[]) => unknown)(...args);
  });
};
const tab = (label: string) =>
  [...container.querySelectorAll('[role="tab"]')].find(
    (t) => t.textContent === label,
  ) as HTMLElement;

describe('DataAnalysis page', () => {
  it('heads the page, and opens on 摘要分析', async () => {
    await render();
    expect(text()).toContain('資料分析 Library');
    expect(text()).toContain('Live Demo');
    expect(text()).toContain('探索資料結構（Schema）、執行 AI 摘要分析，並深入查看明細資料。');
    expect(text()).toContain('[SummarizeTab]');
    expect(text()).not.toContain('[SchemaTab]');
    expect(tab('摘要分析').getAttribute('aria-selected')).toBe('true');
  });

  it('the tabs switch panels, and the details hook knows which is open', async () => {
    await render();
    expect(mocks.detailsArgs).toHaveBeenLastCalledWith(expect.objectContaining({ tab: 1 }));
    await act(async () => tab('Schema').click());
    expect(text()).toContain('[SchemaTab]');
    expect(text()).not.toContain('[SummarizeTab]');
    await act(async () => tab('明細資料').click());
    expect(text()).toContain('[DetailsTab]');
    expect(mocks.detailsArgs).toHaveBeenLastCalledWith(expect.objectContaining({ tab: 2 }));
  });

  it('signed in or not decides the sources and whether a result can go on a dashboard', async () => {
    await render();
    expect(mocks.sourceArgs).toHaveBeenLastCalledWith(true);
    expect(props('SourcePanel').isLoggedIn).toBe(true);
    expect(props('SummarizeTab')).toMatchObject({ isLoggedIn: true, canAddToDashboard: true });

    mocks.user.mockReturnValue({ data: undefined });
    await render();
    expect(mocks.sourceArgs).toHaveBeenLastCalledWith(false);
    expect(props('SummarizeTab')).toMatchObject({ isLoggedIn: false, canAddToDashboard: false });
  });
});

describe('DataAnalysis page — the source', () => {
  it('reads the schema of the source and table in use', async () => {
    mocks.source = sourceContext({ ctxSourceId: 'src-1', ctxTableName: 'sales.Daily' });
    await render();
    expect(mocks.schema).toHaveBeenLastCalledWith(
      { sourceId: 'src-1', tableName: 'sales.Daily' },
      { skip: false },
    );
    expect(props('SchemaTab')).toBeUndefined(); // not on screen until its tab is
    await act(async () => tab('Schema').click());
    expect(props('SchemaTab')).toMatchObject({ fields: ordersFields, isLoading: false });
  });

  it('a custom source: says it is queried, by name (or id), with its table', async () => {
    mocks.source = sourceContext({
      activeSourceId: 'src-1',
      activeSource: { id: 'src-1', name: '銷售庫' },
      activeTableName: 'sales.Daily',
    });
    await render();
    expect(text()).toContain(
      '已選擇自訂資料來源 — 正在查詢 銷售庫。連線資訊已加密儲存並透過 HTTPS',
    );
    expect(text()).toContain('查詢中 銷售庫 → sales.Daily');

    mocks.source = sourceContext({ activeSourceId: 'src-9', activeTableName: '' });
    await render();
    expect(text()).toContain('正在查詢 src-9。');
    expect(text()).not.toContain('→');
  });

  it('the built-in source: names its table, or the default one', async () => {
    await render();
    expect(text()).not.toContain('已選擇自訂資料來源');
    expect(text()).toContain('查詢中 dbo.Orders');
    mocks.source = sourceContext({ activeTableName: '' });
    await render();
    expect(text()).toContain('查詢中 dbo.Orders');
  });

  it('picking a source sets its default table, and starts the analysis over', async () => {
    await render();
    await act(async () => tab('Schema').click());
    await call(props('SchemaTab').onDateFieldChange, 'Revenue');
    await call(props('SourcePanel').onSourceChange, 'src-1');
    const source = mocks.source;
    expect(source.setActiveSourceId).toHaveBeenCalledWith('src-1');
    expect(source.setTableNameInput).toHaveBeenCalledWith('sales.Daily');
    expect(source.setActiveTableName).toHaveBeenCalledWith('sales.Daily');
    expect(mocks.summarize.reset).toHaveBeenCalled();
    expect(mocks.details.reset).toHaveBeenCalled();
    expect(props('SchemaTab').dateField).toBe('OrderDate');

    await call(props('SourcePanel').onSourceChange, 'src-2');
    expect(source.setActiveTableName).toHaveBeenLastCalledWith('');
    await call(props('SourcePanel').onSourceChange, '');
    expect(source.setActiveSourceId).toHaveBeenLastCalledWith(null);
    expect(source.setTableNameInput).toHaveBeenLastCalledWith('dbo.Orders');
    expect(source.setActiveTableName).toHaveBeenLastCalledWith('dbo.Orders');
  });

  it('passes the table name box, the manager and the upload through', async () => {
    await render();
    const panel = props('SourcePanel');
    expect(panel).toMatchObject({ tableNameInput: 'dbo.Orders', sessionInfo: null });
    expect(panel.onTableNameChange).toBe(mocks.source.setTableNameInput);
    expect(panel.onApplyTableName).toBe(mocks.source.applyTableName);
    await call(panel.onOpenManager);
    expect(mocks.source.setManagerOpen).toHaveBeenCalledWith(true);
    await call(props('SourceManagerDialog').onClose);
    expect(mocks.source.setManagerOpen).toHaveBeenLastCalledWith(false);
    await call(panel.onOpenUpload);
    expect(mocks.source.setUploadOpen).toHaveBeenCalledWith(true);
    await call(props('UploadDataDialog').onClose);
    expect(mocks.source.setUploadOpen).toHaveBeenLastCalledWith(false);
  });
});

describe('DataAnalysis page — an uploaded file', () => {
  beforeEach(() => {
    mocks.source = sourceContext({ sessionInfo: session });
  });

  it('works on the upload’s own schema and row count, without the schema query or a source line', async () => {
    mocks.schema.mockReturnValue({ data: undefined, isLoading: true, error: { status: 500 } });
    mocks.rowCount.mockReturnValue({ data: undefined, isFetching: true });
    await render();
    expect(mocks.schema).toHaveBeenLastCalledWith(expect.anything(), { skip: true });
    expect(mocks.rowCount).toHaveBeenLastCalledWith(expect.anything(), { skip: true });
    expect(mocks.maxDate).toHaveBeenLastCalledWith(expect.objectContaining({ sessionId: 's-1' }));
    expect(text()).not.toContain('查詢中');
    expect(props('OverviewStrip')).toMatchObject({
      rowCountDisplay: '12,345',
      rowCountLoading: false,
      columnCount: 2,
      schemaLoading: false,
    });
    await act(async () => tab('Schema').click());
    expect(props('SchemaTab')).toMatchObject({
      fields: session.schema,
      isLoading: false,
      error: undefined,
      dateField: 'Day',
    });
    expect(props('SummarizeTab').canAddToDashboard).toBe(false);
  });

  it('even with a custom source picked, an upload says nothing about that source', async () => {
    mocks.source = sourceContext({
      sessionInfo: session,
      activeSourceId: 'src-1',
      activeSource: { id: 'src-1', name: '銷售庫' },
    });
    await render();
    expect(text()).not.toContain('已選擇自訂資料來源');
  });

  it('a new upload starts the analysis over; clearing it too', async () => {
    await render();
    const info = { ...session, sessionId: 's-2' };
    await call(props('UploadDataDialog').onSessionCreated, info);
    expect(mocks.source.setSessionInfo).toHaveBeenCalledWith(info);
    expect(mocks.source.setUploadOpen).toHaveBeenCalledWith(false);
    expect(mocks.summarize.reset).toHaveBeenCalledTimes(1);
    expect(mocks.details.reset).toHaveBeenCalledTimes(1);

    await call(props('SourcePanel').onClearSession);
    expect(mocks.source.clearSession).toHaveBeenCalled();
    expect(mocks.summarize.reset).toHaveBeenCalledTimes(2);
    expect(mocks.details.reset).toHaveBeenCalledTimes(2);
  });
});

describe('DataAnalysis page — overview', () => {
  it('counts rows by the date field when it is a real date column', async () => {
    mocks.source = sourceContext({ ctxSourceId: 'src-1', ctxTableName: 't' });
    await render();
    expect(mocks.rowCount).toHaveBeenLastCalledWith(
      { sourceId: 'src-1', tableName: 't', dateField: 'OrderDate', filters: [] },
      { skip: false },
    );
    expect(props('OverviewStrip')).toMatchObject({
      rowCountDisplay: '98,765',
      columnCount: 3,
      dateField: 'OrderDate',
      maxDate: '2026-10-20',
      maxDateLoading: false,
    });
    expect(mocks.maxDate).toHaveBeenLastCalledWith({
      sessionId: undefined,
      sourceId: 'src-1',
      tableName: 't',
      dateField: 'OrderDate',
    });
  });

  it('a date field that is not a date column counts nothing; no count yet shows nothing', async () => {
    mocks.schema.mockReturnValue({
      data: { data: { fields: [field('Region', 'String'), field('OrderDate', 'String')] } },
      isLoading: false,
    });
    mocks.rowCount.mockReturnValue({ data: undefined, isFetching: true });
    await render();
    expect(mocks.rowCount).toHaveBeenLastCalledWith(expect.objectContaining({ dateField: '' }), {
      skip: true,
    });
    expect(props('OverviewStrip')).toMatchObject({
      rowCountDisplay: undefined,
      rowCountLoading: true,
    });
  });

  it('the schema still loading shows as loading', async () => {
    mocks.schema.mockReturnValue({ data: undefined, isLoading: true, error: undefined });
    await render();
    expect(props('OverviewStrip')).toMatchObject({ schemaLoading: true, columnCount: 0 });
  });
});

describe('DataAnalysis page — asking AI', () => {
  const result = {
    plan: { viewBy: [], measures: [] },
    interpretation: '各地區營收',
    rows: [{ Region: 'EMEA' }],
    notes: ['note'],
  };

  it('asks with the source and the question; the answer and a new version go to the tab', async () => {
    mocks.source = sourceContext({ ctxSourceId: 'src-1', ctxTableName: 't' });
    mocks.nl.mockResolvedValue({ data: { data: result } });
    await render();
    expect(props('SummarizeTab')).toMatchObject({
      nlRows: [],
      nlResultVersion: 0,
      summarizeVersion: 3,
    });
    await call(props('SummarizeTab').onRunNlSummarize, '各地區營收');
    expect(mocks.nl).toHaveBeenCalledWith({
      context: { sourceId: 'src-1', tableName: 't' },
      sessionId: undefined,
      query: '各地區營收',
    });
    expect(props('SummarizeTab')).toMatchObject({
      nlRows: result.rows,
      nlInterpretation: '各地區營收',
      nlPlan: result.plan,
      nlNotes: ['note'],
      nlResultVersion: 1,
    });
  });

  it('the built-in source or an upload sends no context; an upload sends its session', async () => {
    mocks.source = sourceContext({ sessionInfo: session });
    mocks.nl.mockResolvedValue({ data: { data: result } });
    await render();
    await call(props('SummarizeTab').onRunNlSummarize, 'q');
    expect(mocks.nl).toHaveBeenCalledWith({ context: undefined, sessionId: 's-1', query: 'q' });
  });

  it('a failed question clears the last answer and keeps the version', async () => {
    mocks.nl.mockResolvedValueOnce({ data: { data: result } }).mockResolvedValueOnce({ error: {} });
    await render();
    await call(props('SummarizeTab').onRunNlSummarize, 'q1');
    await call(props('SummarizeTab').onRunNlSummarize, 'q2');
    expect(props('SummarizeTab')).toMatchObject({ nlRows: [], nlResultVersion: 1 });
    expect(props('SummarizeTab').nlInterpretation).toBeUndefined();
  });

  it('changing source or clearing an upload drops the answer', async () => {
    mocks.nl.mockResolvedValue({ data: { data: result } });
    await render();
    await call(props('SummarizeTab').onRunNlSummarize, 'q');
    await call(props('SourcePanel').onSourceChange, 'src-1');
    expect(props('SummarizeTab').nlRows).toEqual([]);

    await call(props('SummarizeTab').onRunNlSummarize, 'q');
    await call(props('SourcePanel').onClearSession);
    expect(props('SummarizeTab').nlRows).toEqual([]);
  });

  it('passes the question’s loading and error state', async () => {
    mocks.nlState.isLoading = true;
    mocks.nlState.error = { status: 422 };
    await render();
    expect(props('SummarizeTab')).toMatchObject({ nlIsLoading: true, nlError: { status: 422 } });
  });
});

describe('DataAnalysis page — wiring the tabs', () => {
  it('the summarize tab gets the summarize hook’s state and actions', async () => {
    mocks.source = sourceContext({ ctxSourceId: 'src-1', ctxTableName: 't' });
    await render();
    const p = props('SummarizeTab');
    const s = mocks.summarize;
    expect(mocks.summarizeArgs).toHaveBeenLastCalledWith({
      ctxSourceId: 'src-1',
      ctxTableName: 't',
      fields: ordersFields,
      sessionId: undefined,
    });
    expect(p).toMatchObject({
      onTransformAdd: s.addTransform,
      onTransformUpdate: s.updateTransform,
      onTransformRemove: s.removeTransform,
      onMeasureAdd: s.addMeasure,
      onMeasureRemove: s.removeMeasure,
      onMeasureUpdate: s.updateMeasure,
      onSummarizeFiltersChange: s.setSummarizeFilters,
      onRunSummarize: s.handleRunSummarize,
      summarizeVersion: 3,
      fields: ordersFields,
      sourceId: 'src-1',
      tableName: 't',
    });
  });

  it('adds a group-by on the first field, updates and removes one by id', async () => {
    await render();
    const p = props('SummarizeTab');
    const apply = () =>
      (mocks.summarize.setViewByEntries as ReturnType<typeof vi.fn>).mock.lastCall![0] as (
        prev: { id: number; fieldName: string }[],
      ) => { id: number; fieldName: string }[];
    const prev = [
      { id: 1, fieldName: 'A' },
      { id: 2, fieldName: 'B' },
    ];

    await call(p.onViewByAdd);
    const added = apply()(prev);
    expect(added).toHaveLength(3);
    expect(added[2].fieldName).toBe('Region');

    await call(p.onViewByUpdate, { id: 2, fieldName: 'C' });
    expect(apply()(prev)).toEqual([
      { id: 1, fieldName: 'A' },
      { id: 2, fieldName: 'C' },
    ]);
    await call(p.onViewByRemove, 1);
    expect(apply()(prev)).toEqual([{ id: 2, fieldName: 'B' }]);
  });

  it('the details tab gets the details hook’s state and actions', async () => {
    mocks.source = sourceContext({ ctxSourceId: 'src-1', ctxTableName: 't' });
    await render();
    await act(async () => tab('明細資料').click());
    const d = mocks.details;
    expect(mocks.detailsArgs).toHaveBeenLastCalledWith({
      ctxSourceId: 'src-1',
      ctxTableName: 't',
      tab: 2,
      dateField: 'OrderDate',
      sessionId: undefined,
    });
    expect(props('DetailsTab')).toMatchObject({
      fields: ordersFields,
      onFiltersChange: d.handleDetailsFiltersChange,
      onPageChange: d.setPage,
      onExport: d.exportCsv,
      sourceId: 'src-1',
      tableName: 't',
    });
  });
});
