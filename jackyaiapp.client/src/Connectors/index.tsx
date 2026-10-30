import { Refresh } from '@mui/icons-material';
import { Box, Grid, IconButton, Typography } from '@mui/material';
import Alert, { AlertProps } from '@mui/material/Alert';
import Snackbar, { SnackbarCloseReason } from '@mui/material/Snackbar';
import React, { useEffect } from 'react';
import { useLocation, useSearchParams } from 'react-router-dom';

import AiConnectorsSection from './AiConnectorsSection';
import { callbackNotice } from './callbackNotice';
import ConnectorCard from './ConnectorCard';
import { ACCOUNTS_ANCHOR } from './connectorView';
import MemorySection from './MemorySection';
import SiteMcpSection from './SiteMcpSection';

import {
  useConnectProviderMutation,
  useDisconnectProviderMutation,
  useGetConnectorCatalogQuery,
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
  const { hash } = useLocation();
  const { data: connectors, isLoading, error, refetch } = useGetConnectorStatusQuery();
  // Read here for the callback's notice only: the names it may show (AiConnectorsSection reads the same cache).
  const { data: catalog, isLoading: catalogLoading } = useGetConnectorCatalogQuery();
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
  // Handle OAuth callback results, once the reader's own names are known (the notice names only those),
  // and once per query: the refresh below reads the lists again.
  const handledQuery = React.useRef<string | null>(null);
  useEffect(() => {
    if (isLoading || catalogLoading) return;
    const query = searchParams.toString();
    if (handledQuery.current === query) return;
    handledQuery.current = query;

    const knownNames = [
      ...(connectors ?? []).flatMap((c) => [c.provider, c.providerDisplayName]),
      ...(catalog?.installed ?? []).map((c) => c.displayName),
    ];
    const notice = callbackNotice(searchParams, knownNames);
    if (notice) {
      setState({ open: true, ...notice });
      if (notice.severity === 'success') refreshAll();
    }

    // Clean up URL parameters
    if (searchParams.get('success') || searchParams.get('error')) {
      window.history.replaceState({}, '', '/connectors');
    }
  }, [searchParams, refreshAll, isLoading, catalogLoading, connectors, catalog]);

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

  // Sent to the account connections (`#accounts`, a native connector's card, D-225): they are below what the AI may
  // use, so the page goes there once they are drawn, once per arrival.
  const accountsRef = React.useRef<HTMLHeadingElement>(null);
  const scrolledFor = React.useRef<string | null>(null);
  useEffect(() => {
    if (hash !== `#${ACCOUNTS_ANCHOR}` || isLoading || !accountsRef.current) return;
    if (scrolledFor.current === hash) return;
    scrolledFor.current = hash;
    accountsRef.current.scrollIntoView({ block: 'start' });
  }, [hash, isLoading]);

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
      <MemorySection notify={(message, severity) => setState({ open: true, message, severity })} />
      <SiteMcpSection notify={(message, severity) => setState({ open: true, message, severity })} />
      <Typography
        variant="h6"
        component="h2"
        gutterBottom
        id={ACCOUNTS_ANCHOR}
        ref={accountsRef}
        sx={{ scrollMarginTop: 72 }}
      >
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
