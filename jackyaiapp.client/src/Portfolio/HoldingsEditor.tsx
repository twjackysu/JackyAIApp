import AddIcon from '@mui/icons-material/Add';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutlineOutlined';
import EditIcon from '@mui/icons-material/Edit';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Divider from '@mui/material/Divider';
import IconButton from '@mui/material/IconButton';
import MenuItem from '@mui/material/MenuItem';
import { useTheme } from '@mui/material/styles';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import useMediaQuery from '@mui/material/useMediaQuery';
import { useEffect, useState } from 'react';

import { EditorRow, makeRow, toRow } from './editorTypes';
import { FundRow } from './FundRow';
import { StockRow } from './StockRow';
import { UpdateHistoryDialog } from './UpdateHistoryDialog';

import { useGetHoldingsQuery, useSaveHoldingsMutation } from '@/apis/portfolioApis';
import { PositionInput } from '@/apis/portfolioApis/types';

const today = () => new Date().toISOString().slice(0, 10);

export function HoldingsEditor() {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const [open, setOpen] = useState(false);
  const { data: holdingsRes } = useGetHoldingsQuery();
  const [saveHoldings, { isLoading: isSaving }] = useSaveHoldingsMutation();

  const [effectiveDate, setEffectiveDate] = useState(today());
  const [rows, setRows] = useState<EditorRow[]>([]);

  const hasExisting = (holdingsRes?.data?.positions?.length ?? 0) > 0;

  // Seed editor from latest holdings when opening
  useEffect(() => {
    if (!open) return;
    const data = holdingsRes?.data;
    setEffectiveDate(today()); // new update defaults to today
    setRows((data?.positions ?? []).map(toRow));
  }, [open, holdingsRes]);

  // Load an arbitrary past snapshot's positions into the editor (restore flow).
  const loadFromSnapshot = (positions: PositionInput[]) => {
    setEffectiveDate(today());
    setRows(positions.map(toRow));
    setOpen(true);
  };

  const stocks = rows.filter((r) => r.assetType === 'STOCK');
  const cashes = rows.filter((r) => r.assetType === 'CASH');
  const funds = rows.filter((r) => r.assetType === 'FUND');

  const patchRow = (key: string, patch: Partial<EditorRow>) =>
    setRows((rs) => rs.map((r) => (r._key === key ? { ...r, ...patch } : r)));
  const removeRow = (key: string) => setRows((rs) => rs.filter((r) => r._key !== key));
  const addRow = (type: 'STOCK' | 'CASH' | 'FUND') => setRows((rs) => [...rs, makeRow(type)]);

  const handleSave = async () => {
    const positions = rows.filter((r) => r.quantity > 0).map(({ _key, ...rest }) => rest); // eslint-disable-line @typescript-eslint/no-unused-vars
    await saveHoldings({ effectiveDate, positions });
    setOpen(false);
  };

  return (
    <>
      <Box sx={{ display: 'flex', gap: 1, alignItems: 'center' }}>
        {hasExisting && <UpdateHistoryDialog onLoadSnapshot={loadFromSnapshot} />}
        <Button
          variant="contained"
          startIcon={hasExisting ? <EditIcon /> : <AddIcon />}
          onClick={() => setOpen(true)}
        >
          {hasExisting ? '更新持倉' : '建立持倉'}
        </Button>
      </Box>

      <Dialog
        open={open}
        onClose={() => setOpen(false)}
        maxWidth="md"
        fullWidth
        fullScreen={fullScreen}
      >
        <DialogTitle>更新持倉</DialogTitle>
        <DialogContent
          sx={{ display: 'flex', flexDirection: 'column', gap: 3, pt: '16px !important' }}
        >
          <Box>
            <TextField
              size="small"
              type="date"
              label="生效日期"
              value={effectiveDate}
              onChange={(e) => setEffectiveDate(e.target.value)}
              slotProps={{ inputLabel: { shrink: true } }}
              helperText="這批持倉從這天起算，直到下次更新"
            />
          </Box>

          {/* Stocks */}
          <Section
            title="股票"
            onAdd={() => addRow('STOCK')}
            addLabel="新增股票"
            empty={stocks.length === 0}
          >
            {stocks.map((r) => (
              <StockRow
                key={r._key}
                row={r}
                onChange={(patch) => patchRow(r._key, patch)}
                onRemove={() => removeRow(r._key)}
              />
            ))}
          </Section>

          <Divider />

          {/* Cash */}
          <Section
            title="現金"
            onAdd={() => addRow('CASH')}
            addLabel="新增現金"
            empty={cashes.length === 0}
          >
            {cashes.map((r) => (
              <Box key={r._key} sx={{ display: 'flex', gap: 1, alignItems: 'center' }}>
                <TextField
                  size="small"
                  select
                  label="幣別"
                  value={r.currency}
                  onChange={(e) => patchRow(r._key, { currency: e.target.value })}
                  sx={{ width: 100 }}
                >
                  <MenuItem value="TWD">TWD</MenuItem>
                  <MenuItem value="USD">USD</MenuItem>
                </TextField>
                <TextField
                  size="small"
                  type="number"
                  label="金額"
                  value={r.quantity || ''}
                  onChange={(e) => patchRow(r._key, { quantity: parseFloat(e.target.value) || 0 })}
                  slotProps={{ htmlInput: { min: 0, step: 0.01 } }}
                  sx={{ flex: 1 }}
                />
                <IconButton onClick={() => removeRow(r._key)} size="small">
                  <DeleteOutlineIcon fontSize="small" />
                </IconButton>
              </Box>
            ))}
          </Section>

          <Divider />

          {/* Funds */}
          <Section
            title="基金"
            onAdd={() => addRow('FUND')}
            addLabel="新增基金"
            empty={funds.length === 0}
          >
            {funds.map((r) => (
              <FundRow
                key={r._key}
                row={r}
                onChange={(patch) => patchRow(r._key, patch)}
                onRemove={() => removeRow(r._key)}
              />
            ))}
          </Section>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpen(false)}>取消</Button>
          <Button variant="contained" onClick={handleSave} disabled={isSaving}>
            {isSaving ? '儲存中...' : '儲存快照'}
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}

function Section({
  title,
  onAdd,
  addLabel,
  empty,
  children,
}: {
  title: string;
  onAdd: () => void;
  addLabel: string;
  empty: boolean;
  children: React.ReactNode;
}) {
  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <Typography variant="subtitle2" sx={{ fontWeight: 700 }}>
          {title}
        </Typography>
        <Button size="small" startIcon={<AddIcon />} onClick={onAdd}>
          {addLabel}
        </Button>
      </Box>
      {empty ? (
        <Typography variant="caption" color="text.secondary">
          尚無項目
        </Typography>
      ) : (
        children
      )}
    </Box>
  );
}
