import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  TextField,
} from '@mui/material';
import React from 'react';

interface SyncFromJiraDialogProps {
  open: boolean;
  onClose: () => void;
  onSubmit: () => void;
  jiraDomain: string;
  setJiraDomain: (value: string) => void;
  jiraEmail: string;
  setJiraEmail: (value: string) => void;
  jiraToken: string;
  setJiraToken: (value: string) => void;
  jiraTickets: string;
  setJiraTickets: (value: string) => void;
  jiraSprints: string;
  setJiraSprints: (value: string) => void;
}

const SyncFromJiraDialog: React.FC<SyncFromJiraDialogProps> = ({
  open,
  onClose,
  onSubmit,
  jiraDomain,
  setJiraDomain,
  jiraEmail,
  setJiraEmail,
  jiraToken,
  setJiraToken,
  jiraTickets,
  setJiraTickets,
  jiraSprints,
  setJiraSprints,
}) => {
  return (
    <Dialog open={open} onClose={onClose}>
      <DialogTitle>Sync from Jira</DialogTitle>
      <DialogContent>
        <TextField
          label="Jira Domain"
          fullWidth
          value={jiraDomain}
          onChange={(e) => setJiraDomain(e.target.value)}
          sx={{ mb: 2 }}
        />
        <TextField
          label="Email"
          fullWidth
          value={jiraEmail}
          onChange={(e) => setJiraEmail(e.target.value)}
          sx={{ mb: 2 }}
        />
        <TextField
          label="API Token"
          type="password"
          fullWidth
          value={jiraToken}
          onChange={(e) => setJiraToken(e.target.value)}
          sx={{ mb: 2 }}
          helperText="go to https://id.atlassian.com/manage-profile/security/api-tokens to create a token"
        />
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
      </DialogContent>
      <DialogActions>
        <Button onClick={onSubmit} variant="contained">
          Submit
        </Button>
        <Button onClick={onClose} variant="outlined">
          Cancel
        </Button>
      </DialogActions>
    </Dialog>
  );
};

export default SyncFromJiraDialog;
