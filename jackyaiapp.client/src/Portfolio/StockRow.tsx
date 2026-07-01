import DeleteOutlineIcon from '@mui/icons-material/DeleteOutlineOutlined';
import Autocomplete from '@mui/material/Autocomplete';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import IconButton from '@mui/material/IconButton';
import TextField from '@mui/material/TextField';
import { useEffect, useMemo, useState } from 'react';

import { EditorRow } from './editorTypes';

import { useSearchStocksQuery } from '@/apis/portfolioApis';
import { StockSearchItem } from '@/apis/portfolioApis/types';

interface Props {
  row: EditorRow;
  error?: string | null;
  onChange: (patch: Partial<EditorRow>) => void;
  onRemove: () => void;
}

export function StockRow({ row, error, onChange, onRemove }: Props) {
  const market = row.market || 'TW';
  const [input, setInput] = useState('');
  const [debounced, setDebounced] = useState('');
  const picked = !!row.symbol;

  useEffect(() => {
    const t = setTimeout(() => setDebounced(input.trim()), 400);
    return () => clearTimeout(t);
  }, [input]);

  const { data: res, isFetching } = useSearchStocksQuery(
    { market, keyword: debounced },
    { skip: picked || debounced.length < 1 },
  );

  const options = useMemo<StockSearchItem[]>(() => res?.data ?? [], [res]);

  return (
    <Box sx={{ display: 'flex', gap: 1, alignItems: 'flex-start', flexWrap: 'wrap' }}>
      {picked ? (
        <Box sx={{ flex: 1, minWidth: 200, display: 'flex', alignItems: 'center', minHeight: 40 }}>
          <Chip
            color="primary"
            variant="outlined"
            label={`${row.symbol} · ${row.name}`}
            onDelete={() => {
              setInput('');
              onChange({ symbol: '', name: '' });
            }}
          />
        </Box>
      ) : (
        <Autocomplete<StockSearchItem>
          size="small"
          sx={{ flex: 1, minWidth: 200 }}
          options={options}
          loading={isFetching}
          value={null}
          filterOptions={(x) => x} // server-side filtering
          getOptionLabel={(o) => `${o.symbol} ${o.name}`}
          isOptionEqualToValue={(o, v) => o.symbol === v.symbol}
          onInputChange={(_, v) => setInput(v)}
          onChange={(_, v) => {
            if (v) {
              onChange({ symbol: v.symbol, name: v.name, market });
              setInput('');
            }
          }}
          // Popper must sit above the Dialog (modal z-index) or it gets hidden.
          slotProps={{ popper: { sx: { zIndex: (t) => t.zIndex.modal + 1 } } }}
          noOptionsText={
            debounced.length < 1
              ? market === 'US'
                ? '輸入代號或公司名（如 AAPL / apple）'
                : '輸入股號或名稱（如 2330 / 台積電）'
              : '查無股票'
          }
          renderOption={(props, o) => (
            <li {...props} key={`${o.market}-${o.symbol}`}>
              <Box component="span" sx={{ fontWeight: 700, fontSize: 13, minWidth: 56 }}>
                {o.symbol}
              </Box>
              <Box component="span" sx={{ ml: 1, fontSize: 13 }}>
                {o.name}
              </Box>
            </li>
          )}
          renderInput={(params) => (
            <TextField
              {...params}
              label="股票 (股號或名稱)"
              placeholder={market === 'US' ? 'AAPL' : '2330'}
              error={!!error}
              helperText={error ?? undefined}
              slotProps={{
                ...params.slotProps,
                input: {
                  ...params.slotProps.input,
                  endAdornment: (
                    <>
                      {isFetching ? <CircularProgress size={14} /> : null}
                      {params.slotProps.input.endAdornment}
                    </>
                  ),
                },
              }}
            />
          )}
        />
      )}

      <TextField
        size="small"
        type="number"
        label="股數"
        value={row.quantity || ''}
        onChange={(e) => onChange({ quantity: parseFloat(e.target.value) || 0 })}
        slotProps={{ htmlInput: { min: 0, step: 1 } }}
        error={!!error && picked}
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
