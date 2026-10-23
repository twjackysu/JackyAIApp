import type {
  FieldDefinition,
  MeasureFieldDto,
  SummarizeRequest,
} from '../../../apis/dataAnalysisApis/types';
import type { ActiveFilter, FieldTransformDef, ViewByEntry } from '../../types';

export interface SummarizeTabProps {
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
