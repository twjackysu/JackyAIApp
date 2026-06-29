import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutlineOutlined';
import ErrorIcon from '@mui/icons-material/Error';
import Box from '@mui/material/Box';
import CircularProgress from '@mui/material/CircularProgress';
import IconButton from '@mui/material/IconButton';
import InputAdornment from '@mui/material/InputAdornment';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import { useEffect, useState } from 'react';

import { EditorRow } from './editorTypes';

import { useLookupStockQuery } from '@/apis/portfolioApis';

interface Props {
  row: EditorRow;
  onChange: (patch: Partial<EditorRow>) => void;
  onRemove: () => void;
}

export function StockRow({ row, onChange, onRemove }: Props) {
  const [lookupKey, setLookupKey] = useState<{ market: string; symbol: string } | null>(null);

  useEffect(() => {
    if (!row.symbol) {
      setLookupKey(null);
      return;
    }
    const t = setTimeout(
      () => setLookupKey({ market: row.market || 'TW', symbol: row.symbol.toUpperCase() }),
      600,
    );
    return () => clearTimeout(t);
  }, [row.symbol, row.market]);

  const {
    data: res,
    isFetching,
    isError,
  } = useLookupStockQuery(lookupKey ?? { market: '', symbol: '' }, { skip: !lookupKey });

  const looked = res?.data;

  useEffect(() => {
    if (looked && looked.companyName) {
      onChange({ name: looked.companyName });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [looked?.companyName]);

  const adornment = isFetching ? (
    <CircularProgress size={14} />
  ) : looked ? (
    <CheckCircleIcon fontSize="small" sx={{ color: 'success.main' }} />
  ) : isError && lookupKey ? (
    <ErrorIcon fontSize="small" sx={{ color: 'warning.main' }} />
  ) : null;

  return (
    <Box sx={{ display: 'flex', gap: 1, alignItems: 'flex-start', flexWrap: 'wrap' }}>
      <TextField
        size="small"
        select
        label="市場"
        value={row.market || 'TW'}
        onChange={(e) =>
          onChange({ market: e.target.value, currency: e.target.value === 'US' ? 'USD' : 'TWD' })
        }
        sx={{ width: 88 }}
      >
        <MenuItem value="TW">台股</MenuItem>
        <MenuItem value="US">美股</MenuItem>
      </TextField>
      <TextField
        size="small"
        label="代號"
        placeholder={row.market === 'US' ? 'AAPL' : '2330'}
        value={row.symbol}
        onChange={(e) => onChange({ symbol: e.target.value, name: '' })}
        slotProps={{
          input: {
            endAdornment: adornment && <InputAdornment position="end">{adornment}</InputAdornment>,
          },
        }}
        sx={{ width: 120 }}
      />
      <TextField
        size="small"
        label="名稱"
        value={row.name}
        onChange={(e) => onChange({ name: e.target.value })}
        sx={{ flex: 1, minWidth: 120 }}
      />
      <TextField
        size="small"
        type="number"
        label="股數"
        value={row.quantity || ''}
        onChange={(e) => onChange({ quantity: parseFloat(e.target.value) || 0 })}
        slotProps={{ htmlInput: { min: 0, step: 1 } }}
        sx={{ width: 100 }}
      />
      <TextField
        size="small"
        type="number"
        label="總成本(選填)"
        placeholder="留空不算損益"
        value={row.costBasis ?? ''}
        onChange={(e) =>
          onChange({ costBasis: e.target.value === '' ? null : parseFloat(e.target.value) })
        }
        slotProps={{ htmlInput: { min: 0, step: 0.01 } }}
        sx={{ width: 140 }}
      />
      <IconButton onClick={onRemove} size="small" sx={{ mt: 0.5 }}>
        <DeleteOutlineIcon fontSize="small" />
      </IconButton>
    </Box>
  );
}
