import { Box, Button, TextField } from '@mui/material';
import React, { useState } from 'react';
import StashedTasks from './components/StashedTasks';
import SyncFromJiraDialog from './components/SyncFromJiraDialog';
import TaskPool from './components/TaskPool';
import WorkBar from './components/WorkBar';
import { TASKS } from './constants';
import EffortPlannerContext from './context/EffortPlannerContext';
import { Task } from './types';

export default function EffortPlanner() {
  const [assigned, setAssigned] = useState<Task[]>([]);
  const [savedTasks, setSavedTasks] = useState<Task[]>([]);
  const [peopleCount, setPeopleCount] = useState(1);
  const [daysPerPerson, setDaysPerPerson] = useState(10);
  const [leaveDays, setLeaveDays] = useState(0);
  const [jiraDomain, setJiraDomain] = useState('');
  const [jiraEmail, setJiraEmail] = useState('');
  const [jiraToken, setJiraToken] = useState('');
  const [jiraTickets, setJiraTickets] = useState('');
  const [jiraSprints, setJiraSprints] = useState('');
  const [showJiraDialog, setShowJiraDialog] = useState(false);
  const [excludeSubTasks, setExcludeSubTasks] = useState(true);

  const onSavedTaskDragStart = (e: React.DragEvent<HTMLDivElement>, taskId: number) => {
    e.dataTransfer.setData('text/plain', taskId.toString());
  };

  return (
    <EffortPlannerContext.Provider
      value={{
        assigned,
        setAssigned,
        savedTasks,
        setSavedTasks,
        peopleCount,
        setPeopleCount,
        daysPerPerson,
        setDaysPerPerson,
        leaveDays,
        setLeaveDays,
        excludeSubTasks,
        setExcludeSubTasks,
        jiraDomain,
        setJiraDomain,
        jiraEmail,
        setJiraEmail,
        jiraToken,
        setJiraToken,
        showJiraDialog,
        setShowJiraDialog,
        jiraTickets,
        setJiraTickets,
        jiraSprints,
        setJiraSprints,
        maxDays: peopleCount * daysPerPerson - leaveDays,
        totalDays: assigned.reduce((sum, task) => sum + task.days, 0),
      }}
    >
      <Box sx={{ p: 3, display: 'flex', flexDirection: 'column', gap: 3 }}>
        <Box sx={{ display: 'flex', gap: 3 }}>
          <Box sx={{ flex: 1 }}>
            <TextField
              label="Number of Developers"
              type="number"
              value={peopleCount}
              onChange={(e) => setPeopleCount(parseInt(e.target.value, 10) || 1)}
              sx={{ mr: 2 }}
            />
            <TextField
              label="Work Days per Developer"
              type="number"
              value={daysPerPerson}
              onChange={(e) => setDaysPerPerson(parseFloat(e.target.value) || 1)}
              sx={{ mr: 2 }}
            />
            <TextField
              label="Total Leave Days"
              type="number"
              value={leaveDays}
              onChange={(e) => setLeaveDays(parseFloat(e.target.value) || 0)}
              sx={{ mr: 2 }}
            />
            <Button variant="contained" onClick={() => setShowJiraDialog(true)}>
              Sync from Jira
            </Button>
          </Box>
        </Box>
        <TaskPool tasks={TASKS} onDragStart={onSavedTaskDragStart} />
        <WorkBar />
        <StashedTasks onDragStart={onSavedTaskDragStart} />
        <SyncFromJiraDialog />
      </Box>
    </EffortPlannerContext.Provider>
  );
}
