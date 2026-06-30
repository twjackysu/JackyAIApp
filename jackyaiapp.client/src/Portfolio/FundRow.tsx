import DeleteOutlineIcon from '@mui/icons-material/DeleteOutlineOutlined';
import Autocomplete from '@mui/material/Autocomplete';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import IconButton from '@mui/material/IconButton';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import { useEffect, useMemo, useState } from 'react';

import { EditorRow } from './editorTypes';

import { useSearchFundsQuery } from '@/apis/portfolioApis';
import { FundSearchItem } from '@/apis/portfolioApis/types';

interface Props {
  row: EditorRow;
  onChange: (patch: Partial<EditorRow>) => void;
  onRemove: () => void;
}

export function FundRow({ row, onChange, onRemove }: Props) {
  const [input, setInput] = useState('');
  const [debounced, setDebounced] = useState('');
  const picked = !!row.symbol;

  useEffect(() => {
    const t = setTimeout(() => setDebounced(input.trim()), 500);
    return () => clearTimeout(t);
  }, [input]);

  const { data: res, isFetching } = useSearchFundsQuery(debounced, {
    skip: picked || debounced.length < 2,
  });

  const options = useMemo<FundSearchItem[]>(() => res?.data ?? [], [res]);

  return (
    <Box sx={{ display: 'flex', gap: 1, alignItems: 'flex-start', flexWrap: 'wrap' }}>
      {picked ? (
        <Box sx={{ flex: 1, minWidth: 220, display: 'flex', alignItems: 'center', minHeight: 40 }}>
          <Chip
            color="info"
            variant="outlined"
            label={`${row.symbol} · ${row.name}`}
            onDelete={() => {
              setInput('');
              onChange({ symbol: '', name: '' });
            }}
          />
        </Box>
      ) : (
        <Autocomplete<FundSearchItem>
          size="small"
          sx={{ flex: 1, minWidth: 220 }}
          options={options}
          loading={isFetching}
          value={null}
          filterOptions={(x) => x} // server-side filtering
          getOptionLabel={(o) => o.name}
          isOptionEqualToValue={(o, v) => o.fundId === v.fundId}
          onInputChange={(_, v) => setInput(v)}
          onChange={(_, v) => {
            if (v) {
              onChange({ symbol: v.fundId, name: v.name, market: 'FUND', currency: 'TWD' });
              setInput('');
            }
          }}
          slotProps={{ popper: { sx: { zIndex: (t) => t.zIndex.modal + 1 } } }}
          noOptionsText={debounced.length < 2 ? '輸入基金中文名或代號' : '查無基金'}
          renderOption={(props, o) => (
            <li {...props} key={o.fundId}>
              <Box component="span" sx={{ fontWeight: 700, fontSize: 12, minWidth: 56 }}>
                {o.fundId}
              </Box>
              <Box component="span" sx={{ ml: 1, fontSize: 13 }}>
                {o.name}
              </Box>
            </li>
          )}
          renderInput={(params) => (
            <TextField
              {...params}
              label="基金 (中文名或代號)"
              placeholder="例：安聯台灣科技 / ALI006"
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
        label="單位數"
        value={row.quantity || ''}
        onChange={(e) => onChange({ quantity: parseFloat(e.target.value) || 0 })}
        slotProps={{ htmlInput: { min: 0, step: 0.0001 } }}
        sx={{ width: 110 }}
      />
      <TextField
        size="small"
        type="number"
        label="淨值(選填)"
        placeholder="自動抓取"
        value={row.unitPrice ?? ''}
        onChange={(e) =>
          onChange({ unitPrice: e.target.value === '' ? null : parseFloat(e.target.value) })
        }
        slotProps={{ htmlInput: { min: 0, step: 0.0001 } }}
        sx={{ width: 110 }}
        helperText={picked ? '留空用即時淨值' : ' '}
      />
      <TextField
        size="small"
        select
        label="幣別"
        value={row.currency}
        onChange={(e) => onChange({ currency: e.target.value })}
        sx={{ width: 90 }}
      >
        <MenuItem value="TWD">TWD</MenuItem>
        <MenuItem value="USD">USD</MenuItem>
      </TextField>
      <IconButton onClick={onRemove} size="small" sx={{ mt: 0.5 }}>
        <DeleteOutlineIcon fontSize="small" />
      </IconButton>
    </Box>
  );
}
