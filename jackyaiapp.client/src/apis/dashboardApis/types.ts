import type { SummarizeRequest } from '../dataAnalysisApis/types';

export type DashboardVisibility = 'Private' | 'Public' | 'Restricted';

export interface DashboardSummary {
  id: string;
  name: string;
  widgetCount: number;
  visibility: DashboardVisibility;
  isOwner: boolean;
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
  visibility: DashboardVisibility;
  isOwner: boolean;
  widgets: DashboardWidget[];
}

export interface ShareMember {
  userId: string;
  email?: string | null;
  name?: string | null;
}

export interface ShareSettings {
  visibility: DashboardVisibility;
  members: ShareMember[];
}

export interface AddWidgetRequest {
  title?: string;
  vizType: string;
  plan: SummarizeRequest;
  w: number;
  h: number;
}

export interface WidgetLayout {
  id: string;
  x: number;
  y: number;
  w: number;
  h: number;
}
