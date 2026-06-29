import AddIcon from '@mui/icons-material/Add';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Select from '@mui/material/Select';
import TextField from '@mui/material/TextField';
import { useState } from 'react';

import { useAddTransactionMutation } from '@/apis/portfolioApis';
import { AddTransactionRequest, Market, TransactionType } from '@/apis/portfolioApis/types';

export function TransactionForm() {
  const [open, setOpen] = useState(false);
  const [addTransaction, { isLoading }] = useAddTransactionMutation();

  const today = new Date().toISOString().slice(0, 10);

  const [form, setForm] = useState<AddTransactionRequest>({
    symbol: '',
    market: 'TW',
    companyName: '',
    type: 'BUY',
    date: today,
    quantity: 0,
    price: 0,
    currency: 'TWD',
    fee: 0,
    note: '',
  });

  const handleMarketChange = (market: Market) => {
    setForm((f) => ({
      ...f,
      market,
      currency: market === 'US' ? 'USD' : 'TWD',
    }));
  };

  const handleSubmit = async () => {
    if (!form.symbol || !form.companyName || form.quantity <= 0 || form.price <= 0) return;
    await addTransaction({
      ...form,
      symbol: form.symbol.toUpperCase(),
    });
    setOpen(false);
    setForm({
      symbol: '',
      market: 'TW',
      companyName: '',
      type: 'BUY',
      date: today,
      quantity: 0,
      price: 0,
      currency: 'TWD',
      fee: 0,
      note: '',
    });
  };

  return (
    <>
      <Button variant="contained" startIcon={<AddIcon />} onClick={() => setOpen(true)}>
        新增交易
      </Button>

      <Dialog open={open} onClose={() => setOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle>新增交易紀錄</DialogTitle>
        <DialogContent
          sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '16px !important' }}
        >
          <FormControl size="small">
            <InputLabel>市場</InputLabel>
            <Select
              value={form.market}
              label="市場"
              onChange={(e) => handleMarketChange(e.target.value as Market)}
            >
              <MenuItem value="TW">台股 (TW)</MenuItem>
              <MenuItem value="US">美股 (US)</MenuItem>
            </Select>
          </FormControl>

          <TextField
            size="small"
            label="股票代號"
            placeholder={form.market === 'TW' ? '例：2330' : '例：AAPL'}
            value={form.symbol}
            onChange={(e) => setForm((f) => ({ ...f, symbol: e.target.value }))}
          />

          <TextField
            size="small"
            label="公司名稱"
            placeholder={form.market === 'TW' ? '例：台積電' : '例：Apple Inc.'}
            value={form.companyName}
            onChange={(e) => setForm((f) => ({ ...f, companyName: e.target.value }))}
          />

          <FormControl size="small">
            <InputLabel>交易類型</InputLabel>
            <Select
              value={form.type}
              label="交易類型"
              onChange={(e) => setForm((f) => ({ ...f, type: e.target.value as TransactionType }))}
            >
              <MenuItem value="BUY">買入</MenuItem>
              <MenuItem value="SELL">賣出</MenuItem>
              <MenuItem value="DIVIDEND">配息</MenuItem>
            </Select>
          </FormControl>

          <TextField
            size="small"
            type="date"
            label="交易日期"
            value={form.date}
            onChange={(e) => setForm((f) => ({ ...f, date: e.target.value }))}
            slotProps={{ inputLabel: { shrink: true } }}
          />

          <TextField
            size="small"
            type="number"
            label="股數"
            value={form.quantity || ''}
            onChange={(e) => setForm((f) => ({ ...f, quantity: parseFloat(e.target.value) || 0 }))}
            slotProps={{ htmlInput: { min: 0, step: 1 } }}
          />

          <TextField
            size="small"
            type="number"
            label={`成交價 (${form.currency})`}
            value={form.price || ''}
            onChange={(e) => setForm((f) => ({ ...f, price: parseFloat(e.target.value) || 0 }))}
            slotProps={{ htmlInput: { min: 0, step: 0.01 } }}
          />

          <TextField
            size="small"
            type="number"
            label={`手續費 (${form.currency})`}
            value={form.fee || ''}
            onChange={(e) => setForm((f) => ({ ...f, fee: parseFloat(e.target.value) || 0 }))}
            slotProps={{ htmlInput: { min: 0, step: 0.01 } }}
          />

          <TextField
            size="small"
            label="備註 (選填)"
            value={form.note}
            onChange={(e) => setForm((f) => ({ ...f, note: e.target.value }))}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpen(false)}>取消</Button>
          <Button
            variant="contained"
            onClick={handleSubmit}
            disabled={
              isLoading ||
              !form.symbol ||
              !form.companyName ||
              form.quantity <= 0 ||
              form.price <= 0
            }
          >
            {isLoading ? '儲存中...' : '儲存'}
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
