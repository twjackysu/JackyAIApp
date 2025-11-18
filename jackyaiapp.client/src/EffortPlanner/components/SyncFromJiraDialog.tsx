import DeleteIcon from '@mui/icons-material/Delete';
import {
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  FormControl,
  FormControlLabel,
  IconButton,
  InputLabel,
  MenuItem,
  Popover,
  Select,
  Switch,
  TextField,
  Typography,
} from '@mui/material';
import { useContext, useRef, useState } from 'react';

import {
  useDeleteJiraConfigMutation,
  useGetJiraConfigQuery,
  useLazyPostSearchQuery,
  usePostJiraConfigMutation,
} from '@/apis/jiraApis';

import { TASK } from '../constants';
import EffortPlannerContext from '../context/EffortPlannerContext';

function SyncFromJiraDialog() {
  const {
    selectedJiraConfigId,
    setSelectedJiraConfigId,
    excludeSubTasks,
    setExcludeSubTasks,
    setAssigned,
    assigned,
    setShowJiraDialog,
    showJiraDialog,
  } = useContext(EffortPlannerContext);

  const [postSearch] = useLazyPostSearchQuery();
  const [postJiraConfig] = usePostJiraConfigMutation();
  const [deleteJiraConfig] = useDeleteJiraConfigMutation();
  const { data } = useGetJiraConfigQuery();
  const jiraConfigs = data?.data || [];
  const selectRef = useRef<HTMLSelectElement>(null);
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null);
  const [newDomain, setNewDomain] = useState('');
  const [newEmail, setNewEmail] = useState('');
  const [newToken, setNewToken] = useState('');
  const [jiraTickets, setJiraTickets] = useState('');
  const [jiraSprints, setJiraSprints] = useState('');

  const handleAddConfigClick = () => {
    setAnchorEl(selectRef.current);
  };

  const handlePopoverClose = () => {
    setAnchorEl(null);
    setNewDomain('');
    setNewEmail('');
    setNewToken('');
  };

  const handleSaveConfig = async () => {
    if (!newDomain || !newEmail || !newToken) return;
    await postJiraConfig({
      body: {
        domain: newDomain,
        email: newEmail,
        token: newToken,
      },
    }).unwrap();
    handlePopoverClose();
  };

  const handleSubmit = () => {
    const fetchJiraTasks = async () => {
      const tickets = jiraTickets
        .split(',')
        .map((t) => t.trim())
        .filter(Boolean);
      const sprints = jiraSprints
        .split(',')
        .map((t) => t.trim())
        .filter(Boolean);
      if (!selectedJiraConfigId || (tickets.length === 0 && sprints.length === 0)) return;

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
            jiraConfigId: selectedJiraConfigId,
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
    fetchJiraTasks();
  };
  const handleClose = () => {
    setShowJiraDialog(false);
  };

  const handleDeleteConfig = async (configId: string) => {
    try {
      await deleteJiraConfig(configId).unwrap();
      console.log('Config deleted successfully');
    } catch (error) {
      console.error('Failed to delete config', error);
    }
  };
  return (
    <Dialog open={showJiraDialog} onClose={handleClose}>
      <DialogTitle>Sync from Jira</DialogTitle>
      <DialogContent>
        <FormControl fullWidth sx={{ my: 1 }}>
          <InputLabel>Select Jira Config</InputLabel>
          <Select
            ref={selectRef}
            value={selectedJiraConfigId}
            onChange={(e) => setSelectedJiraConfigId(e.target.value)}
            renderValue={(selected) =>
              selected === 'add-config'
                ? 'Add Config'
                : jiraConfigs.find((config) => config.id === selected)?.id
            }
          >
            {jiraConfigs.map((config) => (
              <MenuItem key={config.id} value={config.id}>
                <Box
                  sx={{
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                    width: '100%',
                  }}
                >
                  <span title={`${config.domain} (${config.email})`}>{config.id}</span>
                  <IconButton
                    edge="end"
                    size="small"
                    onClick={(e) => {
                      e.stopPropagation(); // Prevent triggering the select change
                      handleDeleteConfig(config.id);
                    }}
                  >
                    <DeleteIcon fontSize="small" />
                  </IconButton>
                </Box>
              </MenuItem>
            ))}
            <MenuItem value="add-config" onClick={handleAddConfigClick}>
              Add Config
            </MenuItem>
          </Select>
        </FormControl>

        <Popover
          open={Boolean(anchorEl)}
          anchorEl={anchorEl}
          onClose={handlePopoverClose}
          anchorOrigin={{
            vertical: 'bottom',
            horizontal: 'left',
          }}
        >
          <Box sx={{ p: 2, width: 300 }}>
            <Typography variant="subtitle1" sx={{ mb: 2 }}>
              Add Jira Config
            </Typography>
            <TextField
              label="Jira Domain"
              fullWidth
              value={newDomain}
              onChange={(e) => setNewDomain(e.target.value)}
              sx={{ mb: 2 }}
            />
            <TextField
              label="Email"
              fullWidth
              value={newEmail}
              onChange={(e) => setNewEmail(e.target.value)}
              sx={{ mb: 2 }}
            />
            <TextField
              label="API Token"
              type="password"
              fullWidth
              value={newToken}
              onChange={(e) => setNewToken(e.target.value)}
              sx={{ mb: 2 }}
              helperText="go to https://id.atlassian.com/manage-profile/security/api-tokens to create a token"
            />
            <Button variant="contained" onClick={handleSaveConfig} fullWidth>
              Save
            </Button>
          </Box>
        </Popover>
        <Divider sx={{ my: 2 }} />
        <TextField
          label="Ticket Numbers"
          fullWidth
          value={jiraTickets}
          onChange={(e) => setJiraTickets(e.target.value)}
          placeholder="HUB-4533,HUB-4443"
          helperText="comma separated ticket numbers"
        />
        <TextField
          label="Sprint numbers"
          fullWidth
          value={jiraSprints}
          onChange={(e) => setJiraSprints(e.target.value)}
          placeholder="10623,10623"
          helperText="comma separated sprint numbers"
        />

        <Box sx={{ mt: 3, mb: 2 }}>
          <FormControlLabel
            control={
              <Switch
                checked={excludeSubTasks}
                onChange={(e) => setExcludeSubTasks(e.target.checked)}
                color="primary"
              />
            }
            label="Exclude sub-tasks"
          />
          <Typography variant="caption" color="text.secondary" display="block">
            When enabled, sub-tasks will not be included in query results
          </Typography>
        </Box>
      </DialogContent>
      <DialogActions>
        <Button onClick={handleSubmit} variant="contained">
          Submit
        </Button>
        <Button onClick={handleClose} variant="outlined">
          Cancel
        </Button>
      </DialogActions>
    </Dialog>
  );
}

export default SyncFromJiraDialog;
