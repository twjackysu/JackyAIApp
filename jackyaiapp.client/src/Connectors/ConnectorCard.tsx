import { Refresh } from '@mui/icons-material';
import {
  Box,
  Card,
  CardContent,
  Chip,
  FormControlLabel,
  IconButton,
  Switch,
  Tooltip,
  Typography,
  Alert,
} from '@mui/material';

import { useRefreshProviderTokensMutation } from '@/apis/connectorsApis/connectorsApis';
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

  const handleRefresh = async () => {
    try {
      await refreshTokens(connector.provider).unwrap();
      if (onRefreshSuccess) onRefreshSuccess();
    } catch {
      if (onRefreshError) onRefreshError();
    }
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
      </CardContent>
    </Card>
  );
};

export default ConnectorCard;
