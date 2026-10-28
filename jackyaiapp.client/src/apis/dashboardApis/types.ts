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
  /**
   * Re-runnable summarize plan (context + viewBy + measures + filters), whole for the dashboard's owner only: anyone
   * else gets just `viewBy`, each group's output name (alias, else fieldName) and type (D-195).
   */
  plan: Pick<SummarizeRequest, 'viewBy'> & Partial<SummarizeRequest>;
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
