import Box from '@mui/material/Box';
import { alpha } from '@mui/material/styles';
import Typography from '@mui/material/Typography';
import { Link } from 'react-router-dom';

// The actual contact email/phone (same as registered on ECPay's merchant verification)
// lives on /pricing only, not repeated on every page — see Pricing/index.tsx for why.
const Footer: React.FC = () => (
  <Box
    component="footer"
    sx={{
      borderTop: '1px solid',
      borderColor: 'divider',
      backgroundColor: alpha('#0B0B0D', 0.6),
      mt: 6,
      py: 3,
      px: 2,
      textAlign: 'center',
    }}
  >
    <Typography variant="body2" color="text.secondary">
      <Link to="/pricing" style={{ color: 'inherit' }}>
        收費資訊與聯絡方式 Pricing &amp; Contact
      </Link>
    </Typography>
  </Box>
);

export default Footer;
