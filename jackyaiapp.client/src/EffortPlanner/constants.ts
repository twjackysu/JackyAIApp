import { blue, green, grey, red } from '@mui/material/colors';
import { TaskCardProp } from './types';

export const TASK: Record<string, TaskCardProp> = {
  LOW: { id: 1, label: 'Low (0~1d)', days: 1, color: green[100], key: 'LOW' },
  MEDIUM: { id: 2, label: 'Medium (1~3d)', days: 3, color: blue[100], key: 'MEDIUM' },
  HIGH: { id: 3, label: 'High (3~5d)', days: 5, color: red[100], key: 'HIGH' },
  UNKNOWN: { id: 4, label: 'No effort is set', days: 0, color: grey[500], key: 'UNKNOWN' },
};

export const TASKS: TaskCardProp[] = [
  { ...TASK.LOW },
  { ...TASK.MEDIUM },
  { ...TASK.HIGH },
  { ...TASK.UNKNOWN },
];

export const WORK_BAR = 'workBar';
export const STASHED = 'stashed';
