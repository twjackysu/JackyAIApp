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

/** Said in the menu, which stays open, when signing out did not go through. */
export const LOGOUT_FAILED_NOTE = '登出沒有成功，請再按一次。';

function UserProfileMenu() {
  const [anchorEl, setAnchorEl] = useState<null | HTMLElement>(null);
  const open = Boolean(anchorEl);
  const [logoutFailed, setLogoutFailed] = useState(false);

  const { data: user, isLoading } = useGetUserInfoQuery();

  const handleClick = (event: React.MouseEvent<HTMLButtonElement>) => {
    setAnchorEl(event.currentTarget);
  };

  const handleClose = () => {
    setAnchorEl(null);
    setLogoutFailed(false);
  };

  const handleLogout = async () => {
    setLogoutFailed(false);
    try {
      // fetch resolves on an error status too: only a 2xx means the sign-in cookie is gone.
      const response = await fetch('/api/account/logout', { method: 'POST' });
      if (response.ok) {
        window.location.href = '/';
        return;
      }
    } catch {
      // Offline, or the request never got through; said in the menu below.
    }
    // The menu stays open with the reason, so the reader is not left signed in without knowing.
    setLogoutFailed(true);
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
            {logoutFailed && (
              <Typography
                role="alert"
                variant="caption"
                color="error"
                sx={{ display: 'block', px: 2, pb: 1, maxWidth: 220 }}
              >
                {LOGOUT_FAILED_NOTE}
              </Typography>
            )}
          </>
        )}
      </Menu>
    </Box>
  );
}

export default UserProfileMenu;
