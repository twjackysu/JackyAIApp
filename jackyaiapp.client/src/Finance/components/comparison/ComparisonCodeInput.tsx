import AddIcon from '@mui/icons-material/Add';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';

import { MAX_COMPARISON_CODES } from '../../utils/financeHelpers';

interface ComparisonCodeInputProps {
  codes: string[];
  inputValue: string;
  onInputChange: (value: string) => void;
  onAdd: () => void;
  onRemove: (code: string) => void;
  disabled?: boolean;
}

/** Up to `MAX_COMPARISON_CODES` stock-code chips, Enter-to-add — same pattern as the single-stock search box. */
export const ComparisonCodeInput = ({
  codes,
  inputValue,
  onInputChange,
  onAdd,
  onRemove,
  disabled = false,
}: ComparisonCodeInputProps) => {
  const atLimit = codes.length >= MAX_COMPARISON_CODES;
  return (
    <Stack spacing={1.5}>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
        <TextField
          fullWidth
          variant="outlined"
          size="small"
          placeholder={
            atLimit ? `最多可比較 ${MAX_COMPARISON_CODES} 檔股票` : '輸入股票代碼 (2330, AAPL...)'
          }
          value={inputValue}
          onChange={(e) => onInputChange(e.target.value)}
          onKeyPress={(e) => e.key === 'Enter' && onAdd()}
          disabled={disabled || atLimit}
          sx={{ flexGrow: 1 }}
        />
        <Button
          variant="outlined"
          startIcon={<AddIcon />}
          onClick={onAdd}
          disabled={disabled || atLimit || !inputValue.trim()}
        >
          新增
        </Button>
      </Stack>
      {codes.length > 0 ? (
        <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', rowGap: 1 }}>
          {codes.map((code) => (
            <Chip key={code} label={code} onDelete={disabled ? undefined : () => onRemove(code)} />
          ))}
        </Stack>
      ) : (
        <Typography variant="caption" color="text.secondary">
          輸入 2-3 檔股票代碼進行比較
        </Typography>
      )}
    </Stack>
  );
};

export default ComparisonCodeInput;
