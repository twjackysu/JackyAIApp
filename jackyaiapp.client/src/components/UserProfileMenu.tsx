import LoginIcon from '@mui/icons-material/Login';
import LogoutIcon from '@mui/icons-material/Logout';
import PersonIcon from '@mui/icons-material/Person';
import {
  Avatar,
  Box,
  Button,
  Divider,
  IconButton,
  Menu,
  MenuItem,
  Typography,
} from '@mui/material';
import { useState } from 'react';

import { useGetUserInfoQuery } from '@/apis/accountApis';
import { ActionTooltip } from '@/components/hints';

function UserProfileMenu() {
  const [anchorEl, setAnchorEl] = useState<null | HTMLElement>(null);
  const open = Boolean(anchorEl);

  const { data: user, isLoading } = useGetUserInfoQuery();

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

  return (
    <Box sx={{ ml: 2, display: 'flex', alignItems: 'center' }}>
      {isLoading ? (
        <Typography variant="body2" sx={{ mr: 1 }}>
          Loading...
        </Typography>
      ) : isAuthenticated ? (
        <>
          <ActionTooltip title="Account menu">
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
          </ActionTooltip>
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
        transformOrigin={{ horizontal: 'right', vertical: 'top' }}
        anchorOrigin={{ horizontal: 'right', vertical: 'bottom' }}
        slotProps={{
          list: {
            'aria-labelledby': 'user-button',
          },
        }}
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
          <>
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
