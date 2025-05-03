import {
  useGetJiraConfigQuery,
  useLazyPostSearchQuery,
  usePostJiraConfigMutation,
} from '@/apis/jiraApis';
import {
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControl,
  FormControlLabel,
  InputLabel,
  MenuItem,
  Popover,
  Select,
  Switch,
  TextField,
  Typography,
} from '@mui/material';
import { useContext, useState } from 'react';
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
  const { data } = useGetJiraConfigQuery();
  const jiraConfigs = data?.data || [];

  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null);
  const [newDomain, setNewDomain] = useState('');
  const [newEmail, setNewEmail] = useState('');
  const [newToken, setNewToken] = useState('');
  const [jiraTickets, setJiraTickets] = useState('');
  const [jiraSprints, setJiraSprints] = useState('');

  const handleAddConfigClick = (event: React.MouseEvent<HTMLElement>) => {
    setAnchorEl(event.currentTarget);
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
  return (
    <Dialog open={showJiraDialog} onClose={handleClose}>
      <DialogTitle>Sync from Jira</DialogTitle>
      <DialogContent>
        {jiraConfigs.length > 0 ? (
          <FormControl fullWidth sx={{ my: 1 }}>
            <InputLabel>Select Jira Config</InputLabel>
            <Select
              value={selectedJiraConfigId}
              onChange={(e) => setSelectedJiraConfigId(e.target.value)}
            >
              {jiraConfigs.map((config) => (
                <MenuItem key={config.id} value={config.id}>
                  {config.domain} ({config.email})
                </MenuItem>
              ))}
            </Select>
          </FormControl>
        ) : (
          <Typography color="text.secondary" sx={{ my: 1 }}>
            Please add a Jira configuration.
          </Typography>
        )}
        <Button variant="outlined" onClick={handleAddConfigClick} sx={{ mb: 1 }}>
          Add Config
        </Button>

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
