import { ContentCopy, Refresh } from '@mui/icons-material';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  FormControlLabel,
  IconButton,
  Snackbar,
  Switch,
  Tooltip,
  Typography,
} from '@mui/material';
import { useState } from 'react';

import {
  useLazyGetAccessTokenQuery,
  useRefreshProviderTokensMutation,
} from '@/apis/connectorsApis/connectorsApis';
import { ConnectorStatus } from '@/apis/connectorsApis/types';

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
  onRefreshSuccess?: () => void;
  onRefreshError?: () => void;
}

const ConnectorCard: React.FC<ConnectorCardProps> = ({
  connector,
  onToggle,
  isLoading,
  onRefreshSuccess,
  onRefreshError,
}) => {
  const [refreshTokens] = useRefreshProviderTokensMutation();
  const [getAccessToken, { isFetching: isGettingToken }] = useLazyGetAccessTokenQuery();
  const [snackbar, setSnackbar] = useState<{ open: boolean; message: string; severity: 'success' | 'error' }>({
    open: false,
    message: '',
    severity: 'success',
  });

  const handleRefresh = async () => {
    try {
      await refreshTokens(connector.provider).unwrap();
      if (onRefreshSuccess) onRefreshSuccess();
    } catch {
      if (onRefreshError) onRefreshError();
    }
  };

  const handleCopyToken = async () => {
    try {
      const result = await getAccessToken(connector.provider).unwrap();
      await navigator.clipboard.writeText(result.accessToken);
      setSnackbar({
        open: true,
        message: 'Access token copied to clipboard!',
        severity: 'success',
      });
    } catch {
      setSnackbar({
        open: true,
        message: 'Failed to get access token. Please try reconnecting.',
        severity: 'error',
      });
    }
  };

  const handleCloseSnackbar = () => {
    setSnackbar((prev) => ({ ...prev, open: false }));
  };

  return (
    <>
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

          {connector.isConnected && (
            <Button
              variant="outlined"
              size="small"
              startIcon={<ContentCopy />}
              onClick={handleCopyToken}
              disabled={isGettingToken}
              fullWidth
              sx={{ mb: 1 }}
            >
              {isGettingToken ? 'Getting Token...' : 'Copy Access Token'}
            </Button>
          )}

          {connector.isConnected && connector.expiresAt && (
            <Typography variant="caption" color="text.secondary" display="block">
              Expires: {new Date(connector.expiresAt).toLocaleDateString()}
            </Typography>
          )}

          {connector.requiresReconnection && (
            <Alert severity="warning" sx={{ mt: 1 }}>
              Connection expires soon. Please reconnect.
            </Alert>
          )}
        </CardContent>
      </Card>

      <Snackbar
        open={snackbar.open}
        autoHideDuration={3000}
        onClose={handleCloseSnackbar}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert onClose={handleCloseSnackbar} severity={snackbar.severity} sx={{ width: '100%' }}>
          {snackbar.message}
        </Alert>
      </Snackbar>
    </>
  );
};

export default ConnectorCard;
