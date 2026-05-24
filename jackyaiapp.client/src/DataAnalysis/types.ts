import type { DateGranularity, GroupFieldType } from '../apis/dataAnalysisApis/types';

export interface ActiveFilter {
  id: number;
  fieldName: string;
  values: string[];
}

export interface BinEntry {
  id: number;
  min: string;
  max: string;
  label: string;
}

export interface GroupEntry {
  id: number;
  label: string;
  values: string[];
}

export interface ViewByEntry {
  id: number;
  fieldName: string;
  type: GroupFieldType;
  alias: string;
  granularity: DateGranularity;
  fallback: string;
  bins: BinEntry[];
  groups: GroupEntry[];
}
