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

/** Site name, as the server also uses it for each page's link-preview title
 * (JackyAIApp.Server/Common/SitePages.cs). */
export const SITE_NAME = 'Jacky Studio';
/** Title of the portal (home) page; per-app pages are titled `${app.name} | ${SITE_NAME}`. */
export const SITE_HOME_TITLE = 'Jacky Studio — AI 學習、投資與實用工具';

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
  /** Short description shown on the portal catalog card — and in link previews, via the
   * server's copy in SitePages.cs. */
  description: string;
  /**
   * A self-contained tool with nothing tied to an account. Hides the account menu, which does
   * nothing on such a page.
   */
  standalone?: boolean;
}

export const PORTFOLIO: AppDef = {
  name: 'Wealth Portfolio',
  path: '/portfolio',
  icon: <PieChartIcon fontSize="small" />,
  allPaths: ['/portfolio'],
  color: '#F59E0B',
  description: '投資組合與資產追蹤',
};

export const FINANCE: AppDef = {
  name: 'Stock Intelligence',
  path: '/finance',
  icon: <AttachMoneyIcon fontSize="small" />,
  allPaths: ['/finance'],
  color: '#16A34A',
  description: 'AI 股票分析與市場情報',
};

export const ENGLISH_LEARNING: AppDef = {
  name: 'English Coach',
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
  description: 'AI 英文學習與單字訓練',
};

export const PDF_UNLOCKER: AppDef = {
  name: 'PDF Unlocker & Compressor',
  path: '/pdf',
  icon: <DescriptionIcon fontSize="small" />,
  allPaths: ['/pdf'],
  color: '#DC2626',
  description: '解除 PDF 密碼保護並壓縮檔案大小。',
  // The unlock endpoint is anonymous — nothing here is tied to an account.
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
  name: 'Job Finder',
  path: '/jobs',
  icon: <WorkIcon fontSize="small" />,
  allPaths: ['/jobs'],
  color: '#9333EA',
  description: '職缺搜尋與工作媒合',
};

export const DATA_ANALYSIS: AppDef = {
  name: 'Data Analysis',
  path: '/data-analysis',
  icon: <AssessmentIcon fontSize="small" />,
  allPaths: ['/data-analysis'],
  color: '#5E6AD2',
  description: 'AI 資料分析與自然語言查詢',
};

export const DASHBOARDS: AppDef = {
  name: 'Data Dashboard',
  path: '/dashboards',
  icon: <DashboardIcon fontSize="small" />,
  allPaths: ['/dashboards'],
  color: '#0EA5E9',
  description: '資料視覺化與互動儀表板',
};

export const CONNECTORS: AppDef = {
  name: 'Connectors',
  path: '/connectors',
  icon: <LinkIcon fontSize="small" />,
  allPaths: ['/connectors'],
  color: '#0D9488',
  description: '讓 AI 使用你的工具與服務。',
};

// The four standalone applications.
export const apps: AppDef[] = [PORTFOLIO, FINANCE, ENGLISH_LEARNING, JOB_SEARCH, DATA_ANALYSIS];

// Everything reachable from the home portal — the ONLY place users switch between sites.
// CONNECTORS is back (docs/ai-native DECISIONS D-011/D-020): it is where the reader lets the AI use
// their tools and MCP servers. It had been hidden over the Google account connection's scopes, which
// are unchanged; drop it from this list to hide it again.
export const portalDestinations: AppDef[] = [...apps, DASHBOARDS, CONNECTORS];

// Every brandable destination, including ones not surfaced in the portal (e.g. PDF).
const allAppDefs: AppDef[] = [...portalDestinations, PDF_UNLOCKER, SLIDESHOW];

/** Resolve which app a given pathname belongs to, or null for the portal / neutral routes. */
export const getCurrentApp = (path: string): AppDef | null =>
  allAppDefs.find((app) =>
    app.allPaths.some((route) => path === route || path.startsWith(`${route}/`)),
  ) ?? null;
