import React, { useEffect } from 'react';
import {
  Box,
  Typography,
  Card,
  CardContent,
  Switch,
  FormControlLabel,
  Chip,
  Grid,
  CircularProgress,
  IconButton,
  Tooltip,
} from '@mui/material';
import { Refresh } from '@mui/icons-material';
import { useSearchParams } from 'react-router-dom';
import {
  useGetConnectorStatusQuery,
  useConnectProviderMutation,
  useDisconnectProviderMutation,
  useRefreshProviderTokensMutation,
} from '@/apis/connectorsApis/connectorsApis';
import { ConnectorStatus } from '@/apis/connectorsApis/types';
import Snackbar, { SnackbarCloseReason } from '@mui/material/Snackbar';
import Alert, { AlertProps } from '@mui/material/Alert';

interface State {
  open: boolean;
  message: string;
  severity?: AlertProps['severity'] | null;
}

// Provider icons (you can replace these with actual icons)
const getProviderIcon = (provider: string) => {
  switch (provider) {
    case 'Microsoft':
      return '🔷';
    case 'Atlassian':
      return '🔵';
    case 'Google':
      return '🔴';
    default:
      return '⚪';
  }
};

interface ConnectorCardProps {
  connector: ConnectorStatus;
  onToggle: (provider: string, isConnected: boolean) => void;
  isLoading: boolean;
}

const ConnectorCard: React.FC<ConnectorCardProps> = ({ connector, onToggle, isLoading }) => {
  const [state, setState] = React.useState<State>({
    open: false,
    message: '',
  });
  const [refreshTokens] = useRefreshProviderTokensMutation();

  const handleRefresh = async () => {
    try {
      await refreshTokens(connector.provider).unwrap();
      setState({
        open: true,
        message: `Refreshed tokens for ${connector.providerDisplayName}`,
        severity: 'success',
      });
    } catch (error) {
      setState({
        open: true,
        message: `Failed to refresh tokens for ${connector.providerDisplayName}`,
        severity: 'error',
      });
    }
  };

  const handleClose = (event?: React.SyntheticEvent | Event, reason?: SnackbarCloseReason) => {
    if (reason === 'clickaway') {
      return;
    }

    setState({ ...state, open: false });
  };

  return (
    <Card
      sx={{
        height: '100%',
        border: connector.isConnected ? '2px solid #4caf50' : '1px solid #e0e0e0',
        transition: 'all 0.3s ease',
        '&:hover': {
          boxShadow: 3,
        },
      }}
    >
      <CardContent>
        <Box sx={{ display: 'flex', alignItems: 'center', mb: 2 }}>
          <Typography variant="h4" sx={{ mr: 1 }}>
            {getProviderIcon(connector.provider)}
          </Typography>
          <Box sx={{ flexGrow: 1 }}>
            <Typography variant="h6" component="h2">
              {connector.providerDisplayName}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {connector.services.join(', ')}
            </Typography>
          </Box>
          {connector.isConnected && (
            <Tooltip title="Refresh tokens">
              <IconButton onClick={handleRefresh} size="small">
                <Refresh />
              </IconButton>
            </Tooltip>
          )}
        </Box>

        <FormControlLabel
          control={
            <Switch
              checked={connector.isConnected}
              onChange={(e) => onToggle(connector.provider, e.target.checked)}
              disabled={isLoading}
            />
          }
          label={connector.isConnected ? 'Connected' : 'Not Connected'}
          sx={{ mb: 2 }}
        />

        <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5, mb: 2 }}>
          {connector.services.map((service) => (
            <Chip
              key={service}
              label={service}
              size="small"
              variant="outlined"
              color={connector.isConnected ? 'primary' : 'default'}
            />
          ))}
        </Box>

        {connector.isConnected && connector.expiresAt && (
          <Typography variant="caption" color="text.secondary">
            Expires: {new Date(connector.expiresAt).toLocaleDateString()}
          </Typography>
        )}

        {connector.requiresReconnection && (
          <Alert severity="warning" sx={{ mt: 1 }}>
            Connection expires soon. Please reconnect.
          </Alert>
        )}

        <Snackbar open={state.open} onClose={handleClose} autoHideDuration={6000}>
          <Alert
            onClose={handleClose}
            severity={state.severity ? state.severity : 'info'}
            variant="filled"
            sx={{ width: '100%' }}
          >
            {state.message}
          </Alert>
        </Snackbar>
      </CardContent>
    </Card>
  );
};

const Connectors: React.FC = () => {
  const [searchParams] = useSearchParams();
  const { data: connectors, isLoading, error, refetch } = useGetConnectorStatusQuery();
  const [connectProvider] = useConnectProviderMutation();
  const [disconnectProvider] = useDisconnectProviderMutation();

  // Handle OAuth callback results
  useEffect(() => {
    const success = searchParams.get('success');
    const error = searchParams.get('error');
    const provider = searchParams.get('provider');

    if (success === 'true' && provider) {
      toast.success(`Successfully connected to ${provider}`);
      refetch(); // Refresh the connector status
    } else if (error && provider) {
      const errorMessages: Record<string, string> = {
        access_denied: 'Access was denied by the user',
        invalid_request: 'Invalid request parameters',
        callback_failed: 'Failed to process the authorization',
        internal_error: 'An internal error occurred',
        missing_parameters: 'Missing required parameters',
        invalid_provider: 'Invalid provider specified',
      };
      const message = errorMessages[error] || `Failed to connect to ${provider}`;
      toast.error(message);
    }

    // Clean up URL parameters
    if (success || error) {
      window.history.replaceState({}, '', '/connectors');
    }
  }, [searchParams, refetch]);

  const handleToggle = async (provider: string, shouldConnect: boolean) => {
    try {
      if (shouldConnect) {
        const result = await connectProvider(provider).unwrap();
        // Redirect to OAuth provider
        window.location.href = result.redirectUrl;
      } else {
        await disconnectProvider(provider).unwrap();
        toast.success(`Disconnected from ${provider}`);
      }
    } catch (error: any) {
      const errorMessage = error?.data?.message || error?.message || 'An error occurred';
      toast.error(errorMessage);
    }
  };

  if (isLoading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: 400 }}>
        <CircularProgress />
      </Box>
    );
  }

  if (error) {
    return (
      <Alert severity="error" sx={{ mb: 2 }}>
        Failed to load connector status. Please try again.
      </Alert>
    );
  }

  return (
    <Box sx={{ maxWidth: 1200, mx: 'auto', p: 3 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 4 }}>
        <Box>
          <Typography variant="h4" component="h1" gutterBottom>
            Connected Services
          </Typography>
          <Typography variant="body1" color="text.secondary">
            Connect your external accounts to sync data and enhance your workflow.
          </Typography>
        </Box>
        <Tooltip title="Refresh status">
          <IconButton onClick={() => refetch()}>
            <Refresh />
          </IconButton>
        </Tooltip>
      </Box>

      <Grid container spacing={3}>
        {connectors?.map((connector) => (
          <Grid item xs={12} sm={6} md={4} key={connector.provider}>
            <ConnectorCard connector={connector} onToggle={handleToggle} isLoading={isLoading} />
          </Grid>
        ))}
      </Grid>

      {connectors?.length === 0 && (
        <Box sx={{ textAlign: 'center', py: 8 }}>
          <Typography variant="h6" color="text.secondary">
            No connectors available
          </Typography>
        </Box>
      )}
    </Box>
  );
};

export default Connectors;
