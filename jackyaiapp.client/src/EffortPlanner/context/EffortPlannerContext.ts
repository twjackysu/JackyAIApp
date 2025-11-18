import { createContext } from 'react';

import { Task } from '../types';

import type { Dispatch, SetStateAction } from 'react';

interface EffortPlannerContextProps {
  assigned: Task[];
  setAssigned: Dispatch<SetStateAction<Task[]>>;
  savedTasks: Task[];
  setSavedTasks: Dispatch<SetStateAction<Task[]>>;
  peopleCount: number;
  setPeopleCount: Dispatch<SetStateAction<number>>;
  daysPerPerson: number;
  setDaysPerPerson: Dispatch<SetStateAction<number>>;
  leaveDays: number;
  setLeaveDays: Dispatch<SetStateAction<number>>;
  excludeSubTasks: boolean;
  setExcludeSubTasks: Dispatch<SetStateAction<boolean>>;
  selectedJiraConfigId: string;
  setSelectedJiraConfigId: Dispatch<SetStateAction<string>>;
  showJiraDialog: boolean;
  setShowJiraDialog: Dispatch<SetStateAction<boolean>>;
  maxDays: number;
  totalDays: number;
}

const EffortPlannerContext = createContext<EffortPlannerContextProps>({
  selectedJiraConfigId: '',
  setSelectedJiraConfigId: () => {},
  showJiraDialog: false,
  setShowJiraDialog: () => {},
  assigned: [],
  setAssigned: () => {},
  savedTasks: [],
  setSavedTasks: () => {},
  peopleCount: 1,
  setPeopleCount: () => {},
  daysPerPerson: 10,
  setDaysPerPerson: () => {},
  leaveDays: 0,
  setLeaveDays: () => {},
  excludeSubTasks: true,
  setExcludeSubTasks: () => {},
  maxDays: 0,
  totalDays: 0,
});

export default EffortPlannerContext;
