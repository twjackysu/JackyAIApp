import CheckIcon from '@mui/icons-material/Check';
import Box from '@mui/material/Box';
import CircularProgress from '@mui/material/CircularProgress';
import Paper from '@mui/material/Paper';
import Typography from '@mui/material/Typography';

const NL_STEPS = ['檢查資料中的實際數值', '理解你的問題', '執行查詢'];

/** The staged progress shown while the NL agent runs (see `useNlProgressStep`). */
export function NlProgress({ step }: { step: number }) {
  return (
    <Paper variant="outlined" sx={{ p: 2, mb: 2 }}>
      {NL_STEPS.map((label, i) => (
        <Box key={label} sx={{ display: 'flex', alignItems: 'center', gap: 1.25, py: 0.5 }}>
          {i < step ? (
            <CheckIcon sx={{ fontSize: 16, color: 'success.main' }} />
          ) : i === step ? (
            <CircularProgress size={14} thickness={5} />
          ) : (
            <Box sx={{ width: 16, display: 'flex', justifyContent: 'center' }}>
              <Box sx={{ width: 5, height: 5, borderRadius: '50%', bgcolor: 'text.disabled' }} />
            </Box>
          )}
          <Typography variant="body2" sx={{ color: i <= step ? 'text.primary' : 'text.disabled' }}>
            {label}
          </Typography>
        </Box>
      ))}
    </Paper>
  );
}
