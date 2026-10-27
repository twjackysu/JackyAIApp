import type { SummarizeRequest } from '../dataAnalysisApis/types';

export type DashboardVisibility = 'Private' | 'Public' | 'Restricted' | 'Anyone';

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

/** A member of a Restricted dashboard, as its owner named them (the server sends the email only). */
export interface ShareMember {
  email?: string | null;
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
