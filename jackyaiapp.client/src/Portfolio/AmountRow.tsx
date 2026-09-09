import DeleteOutlineIcon from '@mui/icons-material/DeleteOutlineOutlined';
import Box from '@mui/material/Box';
import IconButton from '@mui/material/IconButton';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';

import { EditorRow } from './editorTypes';

interface Props {
  row: EditorRow;
  error?: string | null;
  /** 金額欄位的標籤：現金／負債是「金額」，不動產是「價值」。 */
  amountLabel: string;
  /** 只有不動產需要——手動估值的方式（市價／買價扣除折舊）。 */
  showValuationMethod?: boolean;
  onChange: (patch: Partial<EditorRow>) => void;
  onRemove: () => void;
}

/**
 * 直接以金額登錄的持倉列（現金、負債、不動產）——沒有代號、沒有報價來源，
 * 使用者填的就是這筆的價值本身。相對地，股票與基金各有自己的搜尋與成本欄位，
 * 因此仍維持 StockRow / FundRow 兩個獨立元件。
 */
export function AmountRow({
  row,
  error,
  amountLabel,
  showValuationMethod,
  onChange,
  onRemove,
}: Props) {
  return (
    // flexWrap so fields stack on mobile instead of overflowing the dialog width and
    // pushing the delete button off-screen; the delete button itself drops size="small"
    // for a real ~44px touch target instead of a hard-to-hit one.
    <Box sx={{ display: 'flex', gap: 1, alignItems: 'flex-start', flexWrap: 'wrap' }}>
      <TextField
        size="small"
        label="名稱"
        value={row.name}
        onChange={(e) => onChange({ name: e.target.value })}
        sx={{ width: 120 }}
      />
      {showValuationMethod && (
        <TextField
          size="small"
          select
          label="估值方式"
          value={row.valuationMethod ?? '市價'}
          onChange={(e) => onChange({ valuationMethod: e.target.value })}
          sx={{ width: 140 }}
        >
          <MenuItem value="市價">市價</MenuItem>
          <MenuItem value="買價扣除折舊">買價扣除折舊</MenuItem>
        </TextField>
      )}
      <TextField
        size="small"
        select
        label="幣別"
        value={row.currency}
        onChange={(e) => onChange({ currency: e.target.value })}
        sx={{ width: 100 }}
      >
        <MenuItem value="TWD">TWD</MenuItem>
        <MenuItem value="USD">USD</MenuItem>
      </TextField>
      <TextField
        size="small"
        type="number"
        label={amountLabel}
        value={row.quantity || ''}
        onChange={(e) => onChange({ quantity: parseFloat(e.target.value) || 0 })}
        slotProps={{ htmlInput: { min: 0, step: 0.01 } }}
        error={!!error}
        helperText={error ?? undefined}
        sx={{ flex: '1 1 140px' }}
      />
      <IconButton onClick={onRemove} sx={{ mt: 0.5 }}>
        <DeleteOutlineIcon fontSize="small" />
      </IconButton>
    </Box>
  );
}
