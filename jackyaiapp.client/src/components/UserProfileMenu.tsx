// filepath: e:\Users\jacky\Documents\Repo\JackyAIApp\jackyaiapp.client\src\components\UserProfileMenu.tsx
import LinkIcon from '@mui/icons-material/Link';
import LoginIcon from '@mui/icons-material/Login';
import LogoutIcon from '@mui/icons-material/Logout';
import MonetizationOnIcon from '@mui/icons-material/MonetizationOn';
import PersonIcon from '@mui/icons-material/Person';
import {
  Avatar,
  Box,
  Button,
  Chip,
  Divider,
  IconButton,
  Menu,
  MenuItem,
  Tooltip,
  Typography,
} from '@mui/material';
import { useState } from 'react';
import { Link } from 'react-router-dom';

import { useGetUserInfoQuery } from '@/apis/accountApis';
import { useGetCreditBalanceQuery } from '@/apis/creditApis';

import { apps } from '../constants/apps';

function UserProfileMenu() {
  const [anchorEl, setAnchorEl] = useState<null | HTMLElement>(null);
  const open = Boolean(anchorEl);

  const { data: user, isLoading } = useGetUserInfoQuery();
  const { data: creditData } = useGetCreditBalanceQuery(undefined, {
    skip: !user?.data,
  });

  const handleClick = (event: React.MouseEvent<HTMLButtonElement>) => {
    setAnchorEl(event.currentTarget);
  };

  const handleClose = () => {
    setAnchorEl(null);
  };

  const handleLogout = async () => {
    try {
      await fetch('/api/account/logout', { method: 'POST' });
      window.location.href = '/';
    } catch (error) {
      console.error('Logout failed:', error);
    }
    handleClose();
  };

  const handleLogin = () => {
    window.location.href = '/api/account/login/Google';
    handleClose();
  };

  const displayName = user?.data.name || user?.data.email || '';
  const isAuthenticated = !!displayName;
  const creditBalance = creditData?.data?.balance ?? user?.data?.creditBalance ?? 0;

  return (
    <Box sx={{ ml: 2, display: 'flex', alignItems: 'center' }}>
      {isLoading ? (
        <Typography variant="body2" sx={{ mr: 1 }}>
          Loading...
        </Typography>
      ) : isAuthenticated ? (
        <>
          <Tooltip title={`Credits: ${creditBalance}`}>
            <Chip
              icon={<MonetizationOnIcon />}
              label={creditBalance}
              size="small"
              color={creditBalance > 0 ? 'success' : 'error'}
              variant="outlined"
              sx={{ mr: 1 }}
            />
          </Tooltip>
          <Tooltip title="Account menu">
            <IconButton
              onClick={handleClick}
              size="small"
              aria-controls={open ? 'user-menu' : undefined}
              aria-haspopup="true"
              aria-expanded={open ? 'true' : undefined}
            >
              <Avatar sx={{ width: 32, height: 32, bgcolor: 'primary.main' }}>
                {displayName.charAt(0).toUpperCase()}
              </Avatar>
            </IconButton>
          </Tooltip>
          <Box sx={{ ml: 1, display: { xs: 'none', sm: 'block' } }}>
            <Typography variant="body2">{displayName}</Typography>
          </Box>
        </>
      ) : (
        <Button size="small" variant="outlined" onClick={handleLogin} startIcon={<LoginIcon />}>
          Login
        </Button>
      )}

      <Menu
        id="user-menu"
        anchorEl={anchorEl}
        open={open}
        onClose={handleClose}
        MenuListProps={{
          'aria-labelledby': 'user-button',
        }}
        transformOrigin={{ horizontal: 'right', vertical: 'top' }}
        anchorOrigin={{ horizontal: 'right', vertical: 'bottom' }}
      >
        {isAuthenticated && (
          <MenuItem disabled>
            <Box sx={{ display: 'flex', alignItems: 'center' }}>
              <PersonIcon sx={{ mr: 1 }} />
              <Typography variant="body2">{displayName}</Typography>
            </Box>
          </MenuItem>
        )}

        {isAuthenticated && (
          <MenuItem disabled>
            <Box sx={{ display: 'flex', alignItems: 'center' }}>
              <MonetizationOnIcon sx={{ mr: 1 }} />
              <Typography variant="body2">
                Credits: <strong>{creditBalance}</strong>
              </Typography>
            </Box>
          </MenuItem>
        )}

        <Divider />

        <Typography variant="subtitle2" sx={{ px: 2, py: 1, fontWeight: 'bold' }}>
          Applications
        </Typography>

        {apps.map((app) => (
          <MenuItem
            key={app.path}
            component={Link}
            to={app.path}
            onClick={handleClose}
            sx={{
              minWidth: 200,
              '&:hover': { backgroundColor: 'action.hover' },
            }}
          >
            <Box sx={{ display: 'flex', alignItems: 'center' }}>
              {app.icon}
              <Typography variant="body2" sx={{ ml: 1 }}>
                {app.name}
              </Typography>
            </Box>
          </MenuItem>
        ))}

        {isAuthenticated && (
          <>
            <Divider />
            <MenuItem
              component={Link}
              to="/connectors"
              onClick={handleClose}
              sx={{
                minWidth: 200,
                '&:hover': { backgroundColor: 'action.hover' },
              }}
            >
              <Box sx={{ display: 'flex', alignItems: 'center' }}>
                <LinkIcon sx={{ mr: 1 }} />
                <Typography variant="body2" sx={{ ml: 1 }}>
                  Connected Services
                </Typography>
              </Box>
            </MenuItem>
            <Divider />
            <MenuItem onClick={handleLogout} sx={{ color: 'error.main' }}>
              <LogoutIcon sx={{ mr: 1 }} />
              Logout
            </MenuItem>
          </>
        )}
      </Menu>
    </Box>
  );
}

export default UserProfileMenu;
