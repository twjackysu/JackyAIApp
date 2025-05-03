import { useLazyPostSearchQuery } from '@/apis/jiraApis';
import { Box, Button, TextField } from '@mui/material';
import React, { useState } from 'react';
import StashedTasks from './components/StashedTasks';
import SyncFromJiraDialog from './components/SyncFromJiraDialog';
import TaskPool from './components/TaskPool';
import WorkBar from './components/WorkBar';
import { TASK, TASKS } from './constants';
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
  const [postSearch] = useLazyPostSearchQuery();

  const maxDays = peopleCount * daysPerPerson - leaveDays;
  const totalDays = assigned.reduce((sum, task) => sum + task.days, 0);

  const onDrop: React.DragEventHandler<HTMLDivElement> = (event) => {
    const taskId = parseInt(event.dataTransfer.getData('text/plain'), 10);
    const task = TASKS.find((t) => t.id === taskId) || savedTasks.find((t) => t.id === taskId);
    if (!task) return;
    const newTotal = totalDays + task.days;
    if (newTotal <= maxDays || (newTotal > maxDays && totalDays <= maxDays)) {
      setAssigned([...assigned, { ...task, name: '' }]);
      setSavedTasks(savedTasks.filter((t) => t.id !== taskId));
    }
  };

  const onStashDrop: React.DragEventHandler<HTMLDivElement> = (event) => {
    const index = parseInt(event.dataTransfer.getData('assigned-index'), 10);
    if (isNaN(index)) return;
    const task = assigned[index];
    if (!task) return;
    const newId = Date.now();
    setSavedTasks([...savedTasks, { ...task, id: newId }]);
    const updated = [...assigned];
    updated.splice(index, 1);
    setAssigned(updated);
  };

  const reset = () => setAssigned([]);

  const onSavedTaskDragStart = (e: React.DragEvent<HTMLDivElement>, taskId: number) => {
    e.dataTransfer.setData('text/plain', taskId.toString());
  };

  const onAssignedDragStart = (e: React.DragEvent<HTMLDivElement>, index: number) => {
    e.dataTransfer.setData('assigned-index', index.toString());
  };

  const deleteAssignedTask = (index: number) => {
    const updated = [...assigned];
    updated.splice(index, 1);
    setAssigned(updated);
  };

  const deleteSavedTask = (taskId: number) => {
    setSavedTasks(savedTasks.filter((t) => t.id !== taskId));
  };

  const fetchJiraTasks = async () => {
    const domain = jiraDomain.trim();
    const email = jiraEmail.trim();
    const token = jiraToken.trim();
    const tickets = jiraTickets
      .split(',')
      .map((t) => t.trim())
      .filter(Boolean);
    const sprints = jiraSprints
      .split(',')
      .map((t) => t.trim())
      .filter(Boolean);
    if (!domain || !email || !token || (tickets.length === 0 && sprints.length === 0)) return;

    const conditions: string[] = [];

    if (tickets.length > 0) {
      const ticketClause = `issuekey in (${tickets.map((t) => t.trim()).join(',')})`;
      conditions.push(ticketClause);
    }

    if (sprints.length > 0) {
      const sprintClause = `sprint in (${sprints.map((s) => s.toString().trim()).join(',')})`;
      conditions.push(sprintClause);
    }
    if (excludeSubTasks) {
      conditions.push('issuetype != Sub-task');
    }
    const jql = conditions.length > 0 ? conditions.join(' AND ') : '';

    try {
      const issues = await postSearch({
        body: {
          email,
          domain,
          token,
          jql,
        },
      }).unwrap();
      if (!issues.data.issues) throw new Error('Jira fetch failed');
      const newTasks = issues.data.issues.map((issue) => {
        const taskCardKey = getTaskCardKeyFromLabels(issue.fields.labels);
        return {
          id: Date.now() + Math.random(),
          name: issue.key,
          label: issue.fields.summary,
          description: issue.fields.description,
          labels: issue.fields.labels,
          days: TASK[taskCardKey].days,
          key: taskCardKey,
        };
      });
      setAssigned([...assigned, ...newTasks]);
      setShowJiraDialog(false);
    } catch (error) {
      console.error('Jira Error', error);
    }
  };
  const getTaskCardKeyFromLabels = (labels: string[]) => {
    if (labels.includes('0_High_Efforts')) return TASK.HIGH.key;
    if (labels.includes('0_Medium_Efforts')) return TASK.MEDIUM.key;
    if (labels.includes('0_Low_Efforts')) return TASK.LOW.key;
    return TASK.UNKNOWN.key;
  };

  return (
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
      <WorkBar
        tasks={assigned}
        maxDays={maxDays}
        totalDays={totalDays}
        onDrop={onDrop}
        onDragStart={onAssignedDragStart}
        onDelete={deleteAssignedTask}
        reset={reset}
      />
      <StashedTasks
        tasks={savedTasks}
        onDrop={onStashDrop}
        onDragStart={onSavedTaskDragStart}
        onDelete={deleteSavedTask}
      />

      <SyncFromJiraDialog
        open={showJiraDialog}
        onClose={() => setShowJiraDialog(false)}
        onSubmit={fetchJiraTasks}
        jiraDomain={jiraDomain}
        setJiraDomain={setJiraDomain}
        jiraEmail={jiraEmail}
        setJiraEmail={setJiraEmail}
        jiraToken={jiraToken}
        setJiraToken={setJiraToken}
        jiraTickets={jiraTickets}
        setJiraTickets={setJiraTickets}
        jiraSprints={jiraSprints}
        setJiraSprints={setJiraSprints}
        excludeSubTasks={excludeSubTasks}
        setExcludeSubTasks={setExcludeSubTasks}
      />
    </Box>
  );
}
