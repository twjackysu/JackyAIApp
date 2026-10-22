import { Refresh } from '@mui/icons-material';
import { Box, Grid, IconButton, Typography } from '@mui/material';
import Alert, { AlertProps } from '@mui/material/Alert';
import Snackbar, { SnackbarCloseReason } from '@mui/material/Snackbar';
import React, { useEffect } from 'react';
import { useSearchParams } from 'react-router-dom';

import ConnectorCard from './ConnectorCard';

import {
  useConnectProviderMutation,
  useDisconnectProviderMutation,
  useGetConnectorStatusQuery,
} from '@/apis/connectorsApis/connectorsApis';
import { CustomConnectRequest } from '@/apis/connectorsApis/types';
import { getApiErrorMessage, getCaughtErrorMessage } from '@/apis/utils/errorMessage';
import CenteredSpinner from '@/components/CenteredSpinner';
import { ActionTooltip } from '@/components/hints';

interface State {
  open: boolean;
  message: string;
  severity?: AlertProps['severity'] | null;
}

const Connectors: React.FC = () => {
  const [searchParams] = useSearchParams();
  const { data: connectors, isLoading, error, refetch } = useGetConnectorStatusQuery();
  const [connectProvider] = useConnectProviderMutation();
  const [disconnectProvider] = useDisconnectProviderMutation();
  const [state, setState] = React.useState<State>({
    open: false,
    message: '',
  });

  const handleClose = (
    _event?: React.SyntheticEvent | Event | null,
    reason?: SnackbarCloseReason,
  ) => {
    if (reason === 'clickaway') {
      return;
    }

    setState({ ...state, open: false });
  };
  // Handle OAuth callback results
  useEffect(() => {
    const success = searchParams.get('success');
    const error = searchParams.get('error');
    const provider = searchParams.get('provider');

    if (success === 'true' && provider) {
      setState({
        open: true,
        message: `Successfully connected to ${provider}`,
        severity: 'success',
      });
      refetch();
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

      setState({ open: true, message, severity: 'error' });
      // setState({ open: true, message, severity: 'error' });
    }

    // Clean up URL parameters
    if (success || error) {
      window.history.replaceState({}, '', '/connectors');
    }
  }, [searchParams, refetch]);

  const handleToggle = async (
    provider: string,
    shouldConnect: boolean,
    customConfig?: CustomConnectRequest,
  ) => {
    try {
      if (shouldConnect) {
        const result = await connectProvider({ provider, customConfig }).unwrap();
        // Redirect to OAuth provider
        window.location.href = result.redirectUrl;
      } else {
        await disconnectProvider(provider).unwrap();

        setState({ open: true, message: `Disconnected from ${provider}`, severity: 'success' });
      }
    } catch (error) {
      setState({ open: true, message: getCaughtErrorMessage(error), severity: 'error' });
    }
  };

  if (isLoading) {
    return <CenteredSpinner sx={{ alignItems: 'center', minHeight: 400 }} />;
  }

  if (error) {
    return (
      <Alert severity="error" sx={{ mb: 2 }}>
        Failed to load connector status: {getApiErrorMessage(error)}
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
        <ActionTooltip title="Refresh status">
          <IconButton onClick={() => refetch()}>
            <Refresh />
          </IconButton>
        </ActionTooltip>
      </Box>
      <Grid container spacing={3}>
        {connectors?.map((connector) => (
          <Grid
            key={connector.provider}
            size={{
              xs: 12,
              sm: 6,
              md: 4,
            }}
          >
            <ConnectorCard
              connector={connector}
              onToggle={handleToggle}
              isLoading={isLoading}
              onRefreshSuccess={() =>
                setState({
                  open: true,
                  message: `Refreshed tokens for ${connector.provider}`,
                  severity: 'success',
                })
              }
              onRefreshError={(message) =>
                setState({
                  open: true,
                  message: `Failed to refresh ${connector.provider}: ${message}`,
                  severity: 'error',
                })
              }
            />
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
    </Box>
  );
};

export default Connectors;
