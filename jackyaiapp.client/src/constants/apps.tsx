import AssessmentIcon from '@mui/icons-material/Assessment';
import AttachMoneyIcon from '@mui/icons-material/AttachMoney';
import DescriptionIcon from '@mui/icons-material/Description';
import LinkIcon from '@mui/icons-material/Link';
import SchoolIcon from '@mui/icons-material/School';
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
}

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
  path: '/dictionary',
  icon: <SchoolIcon fontSize="small" />,
  allPaths: ['/dictionary', '/repository', '/exam', '/daily', '/review', '/stats'],
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
  name: 'Data Analysis Demo',
  path: '/data-analysis',
  icon: <AssessmentIcon fontSize="small" />,
  allPaths: ['/data-analysis'],
  color: '#5E6AD2',
  description: '用自然語言查詢與視覺化資料的即時 Demo。',
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
export const apps: AppDef[] = [FINANCE, ENGLISH_LEARNING, JOB_SEARCH, DATA_ANALYSIS];

// Everything reachable from the home portal — the ONLY place users switch between sites.
export const portalDestinations: AppDef[] = [...apps, CONNECTORS];

// Every brandable destination, including ones not surfaced in the portal (e.g. PDF).
const allAppDefs: AppDef[] = [...portalDestinations, PDF_UNLOCKER];

/** Resolve which app a given pathname belongs to, or null for the portal / neutral routes. */
export const getCurrentApp = (path: string): AppDef | null =>
  allAppDefs.find((app) =>
    app.allPaths.some((route) => path === route || path.startsWith(`${route}/`)),
  ) ?? null;
