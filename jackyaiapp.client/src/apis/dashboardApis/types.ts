import type { SummarizeRequest } from '../dataAnalysisApis/types';

export interface DashboardSummary {
  id: string;
  name: string;
  widgetCount: number;
  createdAt: string;
}

export interface DashboardWidget {
  id: string;
  title?: string | null;
  vizType: string;
  /** Re-runnable summarize plan (context + viewBy + measures + filters). */
  plan: SummarizeRequest;
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface DashboardDetail {
  id: string;
  name: string;
  widgets: DashboardWidget[];
}

export interface AddWidgetRequest {
  title?: string;
  vizType: string;
  plan: SummarizeRequest;
  w: number;
  h: number;
}
