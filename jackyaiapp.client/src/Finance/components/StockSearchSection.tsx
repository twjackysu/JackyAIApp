import SearchIcon from '@mui/icons-material/Search';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import InputAdornment from '@mui/material/InputAdornment';
import LinearProgress from '@mui/material/LinearProgress';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';

interface StockSearchSectionProps {
  stockSearchTerm: string;
  onSearchTermChange: (value: string) => void;
  onSearch: () => void;
  onClear: () => void;
  isAnalyzing: boolean;
  hasResults: boolean;
}

/**
 * Stock search input section with search and clear buttons
 */
export const StockSearchSection = ({
  stockSearchTerm,
  onSearchTermChange,
  onSearch,
  onClear,
  isAnalyzing,
  hasResults,
}: StockSearchSectionProps) => {
  const handleKeyPress = (event: React.KeyboardEvent) => {
    if (event.key === 'Enter') {
      onSearch();
    }
  };

  return (
    <Paper
      sx={{
        p: 3,
        mb: 3,
        bgcolor: 'background.paper',
        border: '1px solid',
        borderColor: 'divider',
      }}
    >
      <Typography variant="h6" gutterBottom sx={{ fontWeight: 'bold', color: 'primary.main' }}>
        股票分析搜尋 (Stock Analysis)
      </Typography>
      <Stack direction="row" spacing={2} alignItems="center">
        <TextField
          fullWidth
          variant="outlined"
          placeholder="輸入股票代碼或公司名稱 (例如: 2330, 台積電)"
          value={stockSearchTerm}
          onChange={(e) => onSearchTermChange(e.target.value)}
          onKeyPress={handleKeyPress}
          disabled={isAnalyzing}
          InputProps={{
            startAdornment: (
              <InputAdornment position="start">
                <SearchIcon />
              </InputAdornment>
            ),
          }}
          sx={{ flexGrow: 1 }}
        />
        <Button
          variant="contained"
          onClick={onSearch}
          disabled={isAnalyzing || !stockSearchTerm.trim()}
          sx={{ minWidth: 120, height: 56 }}
        >
          {isAnalyzing ? <CircularProgress size={20} color="inherit" /> : '分析'}
        </Button>
        {hasResults && (
          <Button
            variant="outlined"
            onClick={onClear}
            disabled={isAnalyzing}
            sx={{ height: 56 }}
          >
            清除
          </Button>
        )}
      </Stack>
      {isAnalyzing && (
        <Box sx={{ mt: 2 }}>
          <LinearProgress />
          <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
            正在分析股票資料並產生趨勢預測...這可能需要 1-2 分鐘
          </Typography>
        </Box>
      )}
    </Paper>
  );
};

export default StockSearchSection;
