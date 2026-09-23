import SearchIcon from '@mui/icons-material/Search';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import InputAdornment from '@mui/material/InputAdornment';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';

import { MAX_COMPARISON_CODES } from '../../utils/comparisonCodes';

interface Props {
  codes: string[];
  inputValue: string;
  onInputChange: (value: string) => void;
  onAdd: () => void;
  onRemove: (code: string) => void;
  onCompare: () => void;
  isComparing: boolean;
}

/** Up to {@link MAX_COMPARISON_CODES} stock-code chips, `Enter`-to-add like the single-stock search box. */
export const ComparisonCodeInput = ({
  codes,
  inputValue,
  onInputChange,
  onAdd,
  onRemove,
  onCompare,
  isComparing,
}: Props) => {
  const atCap = codes.length >= MAX_COMPARISON_CODES;

  return (
    <Stack spacing={1.5}>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
        <TextField
          fullWidth
          variant="outlined"
          size="small"
          placeholder={
            atCap
              ? `最多比較 ${MAX_COMPARISON_CODES} 檔股票`
              : '輸入股票代碼後按 Enter 加入 (2330, AAPL...)'
          }
          value={inputValue}
          onChange={(e) => onInputChange(e.target.value)}
          onKeyPress={(e) => e.key === 'Enter' && onAdd()}
          disabled={isComparing || atCap}
          sx={{ flexGrow: 1 }}
          slotProps={{
            input: {
              startAdornment: (
                <InputAdornment position="start">
                  <SearchIcon />
                </InputAdornment>
              ),
            },
          }}
        />
        <Button
          variant="outlined"
          onClick={onAdd}
          disabled={isComparing || atCap || !inputValue.trim()}
          sx={{ minWidth: { xs: 56, sm: 88 }, height: 40 }}
        >
          加入
        </Button>
      </Stack>
      {codes.length > 0 && (
        <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', gap: 1 }}>
          {codes.map((code) => (
            <Chip
              key={code}
              label={code}
              onDelete={isComparing ? undefined : () => onRemove(code)}
            />
          ))}
        </Stack>
      )}
      <Button
        variant="contained"
        onClick={onCompare}
        disabled={isComparing || codes.length === 0}
        sx={{ alignSelf: 'flex-start', minWidth: 100 }}
      >
        {isComparing ? <CircularProgress size={18} color="inherit" /> : '比較'}
      </Button>
    </Stack>
  );
};
