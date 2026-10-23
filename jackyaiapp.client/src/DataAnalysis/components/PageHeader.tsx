import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';

/** The page's title, its Live Demo badge and what the page is for. */
export function PageHeader() {
  return (
    <Box sx={{ mb: 3 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25 }}>
        <Typography variant="h5" sx={{ color: 'text.primary' }}>
          資料分析 Library
        </Typography>
        <Box
          component="span"
          sx={{
            fontSize: '0.7rem',
            fontWeight: 500,
            letterSpacing: '0.02em',
            color: 'text.secondary',
            border: '1px solid',
            borderColor: 'divider',
            borderRadius: '6px',
            px: 0.75,
            py: 0.25,
            lineHeight: 1.4,
          }}
        >
          Live Demo
        </Box>
      </Box>
      <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
        探索資料結構（Schema）、執行 AI 摘要分析，並深入查看明細資料。
      </Typography>
    </Box>
  );
}
