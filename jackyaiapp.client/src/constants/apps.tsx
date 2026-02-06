import AttachMoneyIcon from '@mui/icons-material/AttachMoney';
import DescriptionIcon from '@mui/icons-material/Description';
import SchoolIcon from '@mui/icons-material/School';

export const FINANCE = {
  name: 'Finance Dashboard',
  path: '/finance',
  icon: <AttachMoneyIcon fontSize="small" />,
  allPaths: ['/finance'],
};

export const ENGLISH_LEARNING = {
  name: 'English Learning',
  path: '/dictionary',
  icon: <SchoolIcon fontSize="small" />,
  allPaths: ['/dictionary', '/repository', '/exam'],
};

export const PDF_UNLOCKER = {
  name: 'PDF Unlocker & Compressor',
  path: '/pdf',
  icon: <DescriptionIcon fontSize="small" />,
  allPaths: ['/pdf'],
};

// Application definitions used for both the app bar and the user profile menu
export const apps = [FINANCE, ENGLISH_LEARNING];
