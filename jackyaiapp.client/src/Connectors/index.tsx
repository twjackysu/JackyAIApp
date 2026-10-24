import { Refresh } from '@mui/icons-material';
import { Box, Grid, IconButton, Typography } from '@mui/material';
import Alert, { AlertProps } from '@mui/material/Alert';
import Snackbar, { SnackbarCloseReason } from '@mui/material/Snackbar';
import React, { useEffect } from 'react';
import { useSearchParams } from 'react-router-dom';

import AiConnectorsSection from './AiConnectorsSection';
import { callbackNotice } from './callbackNotice';
import ConnectorCard from './ConnectorCard';
import SiteMcpSection from './SiteMcpSection';

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
  // The AI part keeps its own data; this tells it to read it again with the account status.
  const [refreshSignal, setRefreshSignal] = React.useState(0);
  const refreshAll = React.useCallback(() => {
    refetch();
    setRefreshSignal((n) => n + 1);
  }, [refetch]);
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
    const notice = callbackNotice(searchParams);
    if (notice) {
      setState({ open: true, ...notice });
      if (notice.severity === 'success') refreshAll();
    }

    // Clean up URL parameters
    if (searchParams.get('success') || searchParams.get('error')) {
      window.history.replaceState({}, '', '/connectors');
    }
  }, [searchParams, refreshAll]);

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
            Connectors
          </Typography>
          <Typography variant="body1" color="text.secondary">
            讓 AI 使用你的工具與服務。
          </Typography>
        </Box>
        <ActionTooltip title="Refresh status">
          <IconButton onClick={refreshAll}>
            <Refresh />
          </IconButton>
        </ActionTooltip>
      </Box>
      <AiConnectorsSection
        refreshSignal={refreshSignal}
        notify={(message, severity) => setState({ open: true, message, severity })}
      />
      <SiteMcpSection notify={(message, severity) => setState({ open: true, message, severity })} />
      <Typography variant="h6" component="h2" gutterBottom>
        帳號連線（Google／Microsoft／Atlassian）
      </Typography>
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
