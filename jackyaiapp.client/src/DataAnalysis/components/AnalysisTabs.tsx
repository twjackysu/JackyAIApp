import AccountTreeOutlinedIcon from '@mui/icons-material/AccountTreeOutlined';
import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import TableRowsOutlinedIcon from '@mui/icons-material/TableRowsOutlined';
import Box from '@mui/material/Box';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';

/** The Schema / 摘要分析 / 明細資料 tabs. */
export function AnalysisTabs({ tab, onChange }: { tab: number; onChange: (tab: number) => void }) {
  return (
    <Box
      sx={{
        display: 'inline-flex',
        bgcolor: 'rgba(255, 255, 255, 0.03)',
        borderRadius: '10px',
        px: 0.5,
        border: '1px solid',
        borderColor: 'divider',
      }}
    >
      <Tabs value={tab} onChange={(_, v: number) => onChange(v)}>
        <Tab
          icon={<AccountTreeOutlinedIcon fontSize="small" />}
          iconPosition="start"
          label="Schema"
        />
        <Tab icon={<AutoAwesomeIcon fontSize="small" />} iconPosition="start" label="摘要分析" />
        <Tab
          icon={<TableRowsOutlinedIcon fontSize="small" />}
          iconPosition="start"
          label="明細資料"
        />
      </Tabs>
    </Box>
  );
}
