import React, { useEffect } from 'react';
import {
  Box,
  Typography,
  Card,
  CardContent,
  Button,
  Alert,
  Chip,
  Grid,
  List,
  ListItem,
  ListItemText,
  ListItemIcon,
  Divider,
  Paper,
  CircularProgress,
} from '@mui/material';
import {
  CloudQueue as CloudIcon,
  Email as EmailIcon,
  Event as EventIcon,
  Group as GroupIcon,
  Chat as ChatIcon,
  CheckCircle as CheckCircleIcon,
  Error as ErrorIcon,
  Link as LinkIcon,
  LinkOff as LinkOffIcon,
} from '@mui/icons-material';
import { useSearchParams } from 'react-router-dom';
import {
  useGetMicrosoftGraphAuthUrlQuery,
  useGetMicrosoftGraphStatusQuery,
  useDisconnectMicrosoftGraphMutation,
  useGetEmailsQuery,
  useGetCalendarEventsQuery,
  useGetTeamsQuery,
  useGetChatsQuery,
} from '@/apis/microsoftGraphApis';

const MicrosoftGraph: React.FC = () => {
  const [searchParams, setSearchParams] = useSearchParams();
  const connected = searchParams.get('connected');
  const error = searchParams.get('error');

  const {
    data: status,
    isLoading: statusLoading,
    refetch: refetchStatus,
  } = useGetMicrosoftGraphStatusQuery();
  const {
    data: authUrlData,
    isLoading: authUrlLoading,
    error: authUrlError,
    refetch: refetchAuthUrl,
  } = useGetMicrosoftGraphAuthUrlQuery(undefined, {
    skip: status?.data?.isConnected, // 如果已連接就不需要獲取 auth URL
  });
  const [disconnectMutation, { isLoading: disconnecting }] = useDisconnectMicrosoftGraphMutation();

  const { data: emails, isLoading: emailsLoading } = useGetEmailsQuery(undefined, {
    skip: !status?.data?.isConnected,
  });
  const { data: calendar, isLoading: calendarLoading } = useGetCalendarEventsQuery(undefined, {
    skip: !status?.data?.isConnected,
  });
  const { data: teams, isLoading: teamsLoading } = useGetTeamsQuery(undefined, {
    skip: !status?.data?.isConnected,
  });
  const { data: chats, isLoading: chatsLoading } = useGetChatsQuery(undefined, {
    skip: !status?.data?.isConnected,
  });

  useEffect(() => {
    if (connected || error) {
      // Clean up URL parameters
      setSearchParams({});
      refetchStatus();
    }
  }, [connected, error, setSearchParams, refetchStatus]);

  const handleConnect = async () => {
    console.log('Connect clicked', { authUrlData, authUrlLoading, authUrlError });

    // 如果沒有 auth URL，先嘗試獲取
    if (!authUrlData?.data?.authUrl && !authUrlLoading) {
      console.log('Fetching auth URL...');
      try {
        const result = await refetchAuthUrl();
        if (result.data?.data?.authUrl) {
          console.log('Redirecting to:', result.data.data.authUrl);
          window.location.href = result.data.data.authUrl;
          return;
        }
      } catch (err) {
        console.error('Failed to fetch auth URL:', err);
        return;
      }
    }

    if (authUrlData?.data?.authUrl) {
      console.log('Redirecting to:', authUrlData.data.authUrl);
      window.location.href = authUrlData.data.authUrl;
    } else {
      console.error('No auth URL available', { authUrlData, authUrlError });
    }
  };

  const handleDisconnect = async () => {
    try {
      await disconnectMutation().unwrap();
      refetchStatus();
    } catch (err) {
      console.error('Failed to disconnect:', err);
    }
  };

  if (statusLoading) {
    return (
      <Box display="flex" justifyContent="center" alignItems="center" minHeight="200px">
        <CircularProgress />
      </Box>
    );
  }

  return (
    <Box>
      <Typography variant="h4" gutterBottom>
        Microsoft Graph Connector
      </Typography>

      {/* Connection Status */}
      <Card sx={{ mb: 3 }}>
        <CardContent>
          <Box display="flex" alignItems="center" justifyContent="space-between">
            <Box display="flex" alignItems="center" gap={2}>
              <CloudIcon
                fontSize="large"
                color={status?.data?.isConnected ? 'primary' : 'disabled'}
              />
              <Box>
                <Typography variant="h6">
                  {status?.data?.isConnected ? 'Connected' : 'Not Connected'}
                </Typography>
                {status?.data?.isConnected && (
                  <Typography variant="body2" color="text.secondary">
                    Connected at: {new Date(status.data.connectedAt!).toLocaleString()}
                  </Typography>
                )}
              </Box>
            </Box>
            <Box>
              {status?.data?.isConnected ? (
                <Button
                  variant="outlined"
                  color="error"
                  startIcon={<LinkOffIcon />}
                  onClick={handleDisconnect}
                  disabled={disconnecting}
                >
                  {disconnecting ? 'Disconnecting...' : 'Disconnect'}
                </Button>
              ) : (
                <Button
                  variant="contained"
                  startIcon={<LinkIcon />}
                  onClick={handleConnect}
                  disabled={authUrlLoading}
                >
                  {authUrlLoading ? 'Loading...' : 'Connect to Microsoft'}
                </Button>
              )}
            </Box>
          </Box>

          {/* Show granted scopes */}
          {status?.data?.isConnected && status.data.scopes && (
            <Box mt={2}>
              <Typography variant="subtitle2" gutterBottom>
                Granted Permissions:
              </Typography>
              <Box display="flex" flexWrap="wrap" gap={1}>
                {status.data.scopes.map((scope) => (
                  <Chip
                    key={scope}
                    label={scope.replace('https://graph.microsoft.com/', '')}
                    size="small"
                  />
                ))}
              </Box>
            </Box>
          )}
        </CardContent>
      </Card>

      {/* Success/Error Messages */}
      {connected && (
        <Alert severity="success" sx={{ mb: 3 }}>
          Successfully connected to Microsoft Graph!
        </Alert>
      )}
      {error && (
        <Alert severity="error" sx={{ mb: 3 }}>
          Failed to connect to Microsoft Graph. Please try again.
        </Alert>
      )}
      {authUrlError && (
        <Alert severity="error" sx={{ mb: 3 }}>
          Failed to get authorization URL. Please check your authentication.
        </Alert>
      )}

      {/* Data Display */}
      {status?.data?.isConnected && (
        <Grid container spacing={3}>
          {/* Emails */}
          <Grid item xs={12} md={6}>
            <Paper sx={{ p: 2 }}>
              <Box display="flex" alignItems="center" gap={1} mb={2}>
                <EmailIcon color="primary" />
                <Typography variant="h6">Recent Emails</Typography>
              </Box>
              {emailsLoading ? (
                <CircularProgress size={24} />
              ) : emails?.data?.emails ? (
                <List dense>
                  {emails.data.emails.slice(0, 5).map((email, index) => (
                    <React.Fragment key={index}>
                      <ListItem>
                        <ListItemText
                          primary={email.subject}
                          secondary={`From: ${email.from} • ${new Date(email.receivedDateTime).toLocaleDateString()}`}
                        />
                      </ListItem>
                      {index < emails.data.emails.length - 1 && <Divider />}
                    </React.Fragment>
                  ))}
                </List>
              ) : (
                <Typography variant="body2" color="text.secondary">
                  No emails found
                </Typography>
              )}
            </Paper>
          </Grid>

          {/* Calendar */}
          <Grid item xs={12} md={6}>
            <Paper sx={{ p: 2 }}>
              <Box display="flex" alignItems="center" gap={1} mb={2}>
                <EventIcon color="primary" />
                <Typography variant="h6">Upcoming Events</Typography>
              </Box>
              {calendarLoading ? (
                <CircularProgress size={24} />
              ) : calendar?.data?.events ? (
                <List dense>
                  {calendar.data.events.slice(0, 5).map((event, index) => (
                    <React.Fragment key={index}>
                      <ListItem>
                        <ListItemText
                          primary={event.subject}
                          secondary={`${new Date(event.start.dateTime).toLocaleString()} • ${event.location || 'No location'}`}
                        />
                      </ListItem>
                      {index < calendar.data.events.length - 1 && <Divider />}
                    </React.Fragment>
                  ))}
                </List>
              ) : (
                <Typography variant="body2" color="text.secondary">
                  No events found
                </Typography>
              )}
            </Paper>
          </Grid>

          {/* Teams */}
          <Grid item xs={12} md={6}>
            <Paper sx={{ p: 2 }}>
              <Box display="flex" alignItems="center" gap={1} mb={2}>
                <GroupIcon color="primary" />
                <Typography variant="h6">Your Teams</Typography>
              </Box>
              {teamsLoading ? (
                <CircularProgress size={24} />
              ) : teams?.data?.teams ? (
                <List dense>
                  {teams.data.teams.slice(0, 5).map((team, index) => (
                    <React.Fragment key={team.id}>
                      <ListItem>
                        <ListItemText
                          primary={team.displayName}
                          secondary={team.description || 'No description'}
                        />
                      </ListItem>
                      {index < teams.data.teams.length - 1 && <Divider />}
                    </React.Fragment>
                  ))}
                </List>
              ) : (
                <Typography variant="body2" color="text.secondary">
                  No teams found
                </Typography>
              )}
            </Paper>
          </Grid>

          {/* Chats */}
          <Grid item xs={12} md={6}>
            <Paper sx={{ p: 2 }}>
              <Box display="flex" alignItems="center" gap={1} mb={2}>
                <ChatIcon color="primary" />
                <Typography variant="h6">Recent Chats</Typography>
              </Box>
              {chatsLoading ? (
                <CircularProgress size={24} />
              ) : chats?.data?.chats ? (
                <List dense>
                  {chats.data.chats.slice(0, 5).map((chat, index) => (
                    <React.Fragment key={chat.id}>
                      <ListItem>
                        <ListItemText
                          primary={chat.topic}
                          secondary={`${chat.chatType} • ${new Date(chat.lastUpdatedDateTime).toLocaleDateString()}`}
                        />
                      </ListItem>
                      {index < chats.data.chats.length - 1 && <Divider />}
                    </React.Fragment>
                  ))}
                </List>
              ) : (
                <Typography variant="body2" color="text.secondary">
                  No chats found
                </Typography>
              )}
            </Paper>
          </Grid>
        </Grid>
      )}

      {!status?.data?.isConnected && (
        <Card>
          <CardContent>
            <Typography variant="h6" gutterBottom>
              Connect to Microsoft Graph
            </Typography>
            <Typography variant="body1" color="text.secondary" paragraph>
              Connect your Microsoft account to access your Outlook emails, calendar events, Teams,
              and chats.
            </Typography>
            <Typography variant="body2" color="text.secondary" paragraph>
              This integration requires the following permissions:
            </Typography>
            <List dense>
              <ListItem>
                <ListItemIcon>
                  <CheckCircleIcon fontSize="small" color="primary" />
                </ListItemIcon>
                <ListItemText primary="Read your mail" />
              </ListItem>
              <ListItem>
                <ListItemIcon>
                  <CheckCircleIcon fontSize="small" color="primary" />
                </ListItemIcon>
                <ListItemText primary="Read your calendar" />
              </ListItem>
              <ListItem>
                <ListItemIcon>
                  <CheckCircleIcon fontSize="small" color="primary" />
                </ListItemIcon>
                <ListItemText primary="Read your Teams and chats" />
              </ListItem>
              <ListItem>
                <ListItemIcon>
                  <CheckCircleIcon fontSize="small" color="primary" />
                </ListItemIcon>
                <ListItemText primary="Read your basic profile" />
              </ListItem>
            </List>
          </CardContent>
        </Card>
      )}
    </Box>
  );
};

export default MicrosoftGraph;
