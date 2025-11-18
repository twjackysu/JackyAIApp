import { Box, Button, Typography } from '@mui/material';
import { red } from '@mui/material/colors';
import React, { useContext } from 'react';

import { TASKS, WORK_BAR } from '../constants';
import EffortPlannerContext from '../context/EffortPlannerContext';

import TaskCard from './TaskCard';

function WorkBar() {
  const { setAssigned, setSavedTasks, savedTasks, assigned, maxDays, totalDays } =
    useContext(EffortPlannerContext);

  const isOverCapacity = totalDays > maxDays;

  const handleAssignedDragStart = (e: React.DragEvent<HTMLDivElement>, index: number) => {
    e.dataTransfer.setData('assigned-index', index.toString());
  };
  const handleDrop: React.DragEventHandler<HTMLDivElement> = (event) => {
    const taskId = parseInt(event.dataTransfer.getData('text/plain'), 10);
    const task = TASKS.find((t) => t.id === taskId) || savedTasks.find((t) => t.id === taskId);
    if (!task) return;
    const newTotal = totalDays + task.days;
    if (newTotal <= maxDays || (newTotal > maxDays && totalDays <= maxDays)) {
      setAssigned([...assigned, { ...task, name: '' }]);
      setSavedTasks(savedTasks.filter((t) => t.id !== taskId));
    }
  };

  const handleDeleteAssignedTask = (index: number) => {
    const updated = [...assigned];
    updated.splice(index, 1);
    setAssigned(updated);
  };

  const reset = () => setAssigned([]);

  const moveTaskToStashed = (index: number) => {
    const task = assigned[index];
    if (!task) return;

    const newId = Date.now();
    setSavedTasks([...savedTasks, { ...task, id: newId }]);

    const updatedAssigned = [...assigned];
    updatedAssigned.splice(index, 1);
    setAssigned(updatedAssigned);
  };
  return (
    <Box>
      <Typography variant="h6">{maxDays}-Day Work Bar</Typography>
      <Box
        onDragOver={(e) => e.preventDefault()}
        onDrop={handleDrop}
        sx={{
          minHeight: 100,
          p: 2,
          border: isOverCapacity ? `2px dashed ${red[500]}` : '2px dashed #999',
          borderRadius: 1,
          mb: 2,
          backgroundColor: isOverCapacity ? red[50] : 'transparent',
        }}
      >
        <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
          {assigned.map((task, index) => (
            <TaskCard
              key={index}
              task={task}
              onDelete={() => handleDeleteAssignedTask(index)}
              onDragStart={(e) => handleAssignedDragStart(e, index)}
              location={WORK_BAR}
              moveToStashed={() => moveTaskToStashed(index)}
            />
          ))}
        </Box>
      </Box>
      <Typography color={isOverCapacity ? 'error' : 'inherit'}>
        Total Assigned Days: {totalDays} / {maxDays}
        {isOverCapacity && ' (Overloaded!)'}
      </Typography>
      <Button variant="outlined" onClick={reset} sx={{ mt: 1 }}>
        Reset
      </Button>
    </Box>
  );
}

export default WorkBar;
