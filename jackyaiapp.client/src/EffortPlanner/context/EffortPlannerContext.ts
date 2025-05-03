import type { Dispatch, SetStateAction } from 'react';
import { createContext } from 'react';
import { Task } from '../types';

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
  jiraDomain: string;
  setJiraDomain: Dispatch<SetStateAction<string>>;
  jiraEmail: string;
  setJiraEmail: Dispatch<SetStateAction<string>>;
  jiraToken: string;
  setJiraToken: Dispatch<SetStateAction<string>>;
  showJiraDialog: boolean;
  setShowJiraDialog: Dispatch<SetStateAction<boolean>>;
  jiraTickets: string;
  setJiraTickets: Dispatch<SetStateAction<string>>;
  jiraSprints: string;
  setJiraSprints: Dispatch<SetStateAction<string>>;
}

const EffortPlannerContext = createContext<EffortPlannerContextProps>({
  jiraDomain: '',
  setJiraDomain: () => {},
  jiraEmail: '',
  setJiraEmail: () => {},
  jiraToken: '',
  setJiraToken: () => {},
  showJiraDialog: false,
  setShowJiraDialog: () => {},
  jiraTickets: '',
  setJiraTickets: () => {},
  jiraSprints: '',
  setJiraSprints: () => {},
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
});

export default EffortPlannerContext;
