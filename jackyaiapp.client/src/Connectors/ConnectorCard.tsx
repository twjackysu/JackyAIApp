import { ContentCopy, ExpandMore, Refresh } from '@mui/icons-material';
import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
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
  TextField,
  Tooltip,
  Typography,
} from '@mui/material';
import { useState } from 'react';

import {
  useLazyGetAccessTokenQuery,
  useRefreshProviderTokensMutation,
} from '@/apis/connectorsApis/connectorsApis';
import { ConnectorStatus, CustomConnectRequest } from '@/apis/connectorsApis/types';

// Provider icons
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
  onToggle: (provider: string, isConnected: boolean, customConfig?: CustomConnectRequest) => void;
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

  // Custom config state
  const [customConfig, setCustomConfig] = useState<CustomConnectRequest>({
    clientId: '',
    clientSecret: '',
    tenantId: '',
    scopes: '',
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

  const handleToggle = (isConnected: boolean) => {
    // Only pass custom config if any field is filled
    const hasCustomConfig = Object.values(customConfig).some((v) => v && v.trim() !== '');
    onToggle(connector.provider, isConnected, hasCustomConfig ? customConfig : undefined);
  };

  const handleCustomConfigChange = (field: keyof CustomConnectRequest) => (
    e: React.ChangeEvent<HTMLInputElement>
  ) => {
    setCustomConfig((prev) => ({ ...prev, [field]: e.target.value }));
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
                onChange={(e) => handleToggle(e.target.checked)}
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
            <Typography variant="caption" color="text.secondary" display="block" sx={{ mb: 1 }}>
              Expires: {new Date(connector.expiresAt).toLocaleDateString()}
            </Typography>
          )}

          {connector.requiresReconnection && (
            <Alert severity="warning" sx={{ mt: 1, mb: 1 }}>
              Connection expires soon. Please reconnect.
            </Alert>
          )}

          {/* Custom OAuth Config - Only show when not connected */}
          {!connector.isConnected && (
            <Accordion sx={{ mt: 1 }}>
              <AccordionSummary expandIcon={<ExpandMore />}>
                <Typography variant="body2">Advanced Settings</Typography>
              </AccordionSummary>
              <AccordionDetails>
                <Typography variant="caption" color="text.secondary" sx={{ mb: 2, display: 'block' }}>
                  Leave empty to use system defaults
                </Typography>
                <TextField
                  label="Client ID"
                  size="small"
                  fullWidth
                  value={customConfig.clientId}
                  onChange={handleCustomConfigChange('clientId')}
                  sx={{ mb: 1.5 }}
                />
                <TextField
                  label="Client Secret"
                  size="small"
                  fullWidth
                  type="password"
                  value={customConfig.clientSecret}
                  onChange={handleCustomConfigChange('clientSecret')}
                  sx={{ mb: 1.5 }}
                />
                {connector.provider === 'Microsoft' && (
                  <TextField
                    label="Tenant ID"
                    size="small"
                    fullWidth
                    value={customConfig.tenantId}
                    onChange={handleCustomConfigChange('tenantId')}
                    sx={{ mb: 1.5 }}
                  />
                )}
                <TextField
                  label="Scopes"
                  size="small"
                  fullWidth
                  placeholder="space-separated scopes"
                  value={customConfig.scopes}
                  onChange={handleCustomConfigChange('scopes')}
                  helperText="Optional: Override default OAuth scopes"
                />
              </AccordionDetails>
            </Accordion>
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
