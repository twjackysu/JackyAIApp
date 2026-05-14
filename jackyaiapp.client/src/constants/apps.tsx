import AttachMoneyIcon from '@mui/icons-material/AttachMoney';
import DescriptionIcon from '@mui/icons-material/Description';
import SchoolIcon from '@mui/icons-material/School';
import WorkIcon from '@mui/icons-material/Work';

export const FINANCE = {
  name: 'Stock Intelligence & Market Analysis',
  path: '/finance',
  icon: <AttachMoneyIcon fontSize="small" />,
  allPaths: ['/finance'],
};

export const ENGLISH_LEARNING = {
  name: 'English Learning',
  path: '/dictionary',
  icon: <SchoolIcon fontSize="small" />,
  allPaths: ['/dictionary', '/repository', '/exam', '/daily', '/review', '/stats'],
};

export const PDF_UNLOCKER = {
  name: 'PDF Unlocker & Compressor',
  path: '/pdf',
  icon: <DescriptionIcon fontSize="small" />,
  allPaths: ['/pdf'],
};

export const JOB_SEARCH = {
  name: '職缺搜尋 (Job Search)',
  path: '/jobs',
  icon: <WorkIcon fontSize="small" />,
  allPaths: ['/jobs'],
};

// Application definitions used for both the app bar and the user profile menu
export const apps = [FINANCE, ENGLISH_LEARNING, JOB_SEARCH];
