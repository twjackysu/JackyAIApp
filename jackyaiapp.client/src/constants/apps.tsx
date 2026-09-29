import AssessmentIcon from '@mui/icons-material/Assessment';
import AttachMoneyIcon from '@mui/icons-material/AttachMoney';
import DashboardIcon from '@mui/icons-material/Dashboard';
import DescriptionIcon from '@mui/icons-material/Description';
import LinkIcon from '@mui/icons-material/Link';
import PieChartIcon from '@mui/icons-material/PieChart';
import SchoolIcon from '@mui/icons-material/School';
import SlideshowIcon from '@mui/icons-material/Slideshow';
import WorkIcon from '@mui/icons-material/Work';

import type { SvgIconProps } from '@mui/material/SvgIcon';
import type { ReactElement } from 'react';

export interface AppDef {
  /** Display name shown in the branded app bar and the portal catalog. */
  name: string;
  /** Entry route for the app. */
  path: string;
  /** Icon used in the app bar and portal card. */
  icon: ReactElement<SvgIconProps>;
  /** Every route that belongs to this app (used to resolve the current app). */
  allPaths: string[];
  /** Brand accent colour — gives each app its own identity. */
  color: string;
  /** Short description shown on the portal catalog card. */
  description: string;
  /**
   * A self-contained tool with nothing tied to an account and nothing to buy. Hides the account
   * menu and the pricing/contact footer, which do nothing on such a page.
   */
  standalone?: boolean;
}

export const PORTFOLIO: AppDef = {
  name: 'My Portfolio',
  path: '/portfolio',
  icon: <PieChartIcon fontSize="small" />,
  allPaths: ['/portfolio'],
  color: '#F59E0B',
  description: '追蹤台美股持倉、損益與資產淨值變化。',
};

export const FINANCE: AppDef = {
  name: 'Stock Intelligence & Market Analysis',
  path: '/finance',
  icon: <AttachMoneyIcon fontSize="small" />,
  allPaths: ['/finance'],
  color: '#16A34A',
  description: '即時股市情報、財報摘要與 AI 市場分析。',
};

export const ENGLISH_LEARNING: AppDef = {
  name: 'English Learning',
  path: '/english/dictionary',
  icon: <SchoolIcon fontSize="small" />,
  allPaths: [
    '/english',
    '/english/dictionary',
    '/english/repository',
    '/english/exam',
    '/english/daily',
    '/english/review',
    '/english/stats',
  ],
  color: '#2563EB',
  description: 'AI 字典、每日挑戰、考試與間隔複習，長期記住單字。',
};

export const PDF_UNLOCKER: AppDef = {
  name: 'PDF Unlocker & Compressor',
  path: '/pdf',
  icon: <DescriptionIcon fontSize="small" />,
  allPaths: ['/pdf'],
  color: '#DC2626',
  description: '解除 PDF 密碼保護並壓縮檔案大小。',
  // The unlock endpoint is anonymous and charges no credits.
  standalone: true,
};

export const SLIDESHOW: AppDef = {
  name: 'Folder Slideshow',
  path: '/slideshow',
  icon: <SlideshowIcon fontSize="small" />,
  allPaths: ['/slideshow'],
  color: '#EC4899',
  description: '選一個資料夾，播放裡面的照片。',
  // No API calls at all — the folder is read in the browser.
  standalone: true,
};

export const JOB_SEARCH: AppDef = {
  name: '職缺搜尋 (Job Search)',
  path: '/jobs',
  icon: <WorkIcon fontSize="small" />,
  allPaths: ['/jobs'],
  color: '#9333EA',
  description: 'AI 職缺搜尋與媒合，找到適合你的工作。',
};

export const DATA_ANALYSIS: AppDef = {
  name: '資料分析 Demo (Data Analysis)',
  path: '/data-analysis',
  icon: <AssessmentIcon fontSize="small" />,
  allPaths: ['/data-analysis'],
  color: '#5E6AD2',
  description: '用自然語言查詢與視覺化資料的即時 Demo。',
};

export const DASHBOARDS: AppDef = {
  name: '儀表板 (Dashboards)',
  path: '/dashboards',
  icon: <DashboardIcon fontSize="small" />,
  allPaths: ['/dashboards'],
  color: '#0EA5E9',
  description: '把 Data Analysis 的分析結果組成可分享的儀表板。',
};

export const CONNECTORS: AppDef = {
  name: 'Connected Services',
  path: '/connectors',
  icon: <LinkIcon fontSize="small" />,
  allPaths: ['/connectors'],
  color: '#0D9488',
  description: '管理已連接的外部資料來源與服務。',
};

// The four standalone applications.
export const apps: AppDef[] = [PORTFOLIO, FINANCE, ENGLISH_LEARNING, JOB_SEARCH, DATA_ANALYSIS];

// Everything reachable from the home portal — the ONLY place users switch between sites.
// CONNECTORS is deliberately excluded: the feature currently has a permission issue with the
// Google MCP scopes and isn't ready to surface to users. Route/branding stay intact for anyone
// hitting /connectors directly.
export const portalDestinations: AppDef[] = [...apps, DASHBOARDS];

// Every brandable destination, including ones not surfaced in the portal (e.g. PDF, Connectors).
const allAppDefs: AppDef[] = [...portalDestinations, CONNECTORS, PDF_UNLOCKER, SLIDESHOW];

/** Resolve which app a given pathname belongs to, or null for the portal / neutral routes. */
export const getCurrentApp = (path: string): AppDef | null =>
  allAppDefs.find((app) =>
    app.allPaths.some((route) => path === route || path.startsWith(`${route}/`)),
  ) ?? null;
