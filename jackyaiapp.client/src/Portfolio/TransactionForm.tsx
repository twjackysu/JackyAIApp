import AddIcon from '@mui/icons-material/Add';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import ErrorIcon from '@mui/icons-material/Error';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormControl from '@mui/material/FormControl';
import InputAdornment from '@mui/material/InputAdornment';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Select from '@mui/material/Select';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useEffect, useState } from 'react';

import { useAddTransactionMutation, useLookupStockQuery } from '@/apis/portfolioApis';
import { AddTransactionRequest, Market, TransactionType } from '@/apis/portfolioApis/types';

const EMPTY_FORM = (today: string): AddTransactionRequest => ({
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

export function TransactionForm() {
  const [open, setOpen] = useState(false);
  const [addTransaction, { isLoading }] = useAddTransactionMutation();

  const today = new Date().toISOString().slice(0, 10);
  const [form, setForm] = useState<AddTransactionRequest>(EMPTY_FORM(today));

  // Debounced symbol for lookup
  const [lookupKey, setLookupKey] = useState<{ market: string; symbol: string } | null>(null);
  useEffect(() => {
    if (!form.symbol || form.symbol.length < 1) {
      setLookupKey(null);
      return;
    }
    const timer = setTimeout(
      () => setLookupKey({ market: form.market, symbol: form.symbol.toUpperCase() }),
      600,
    );
    return () => clearTimeout(timer);
  }, [form.symbol, form.market]);

  const {
    data: lookupRes,
    isFetching: isLooking,
    isError: lookupFailed,
  } = useLookupStockQuery(lookupKey ?? { market: '', symbol: '' }, {
    skip: !lookupKey,
  });

  const lookedUp = lookupRes?.data;

  // Auto-fill companyName and price when lookup succeeds
  useEffect(() => {
    if (!lookedUp) return;
    setForm((f) => ({
      ...f,
      companyName: lookedUp.companyName,
      price: f.price === 0 && lookedUp.currentPrice > 0 ? lookedUp.currentPrice : f.price,
    }));
  }, [lookedUp]);

  const handleMarketChange = (market: Market) => {
    setLookupKey(null);
    setForm((f) => ({
      ...f,
      market,
      currency: market === 'US' ? 'USD' : 'TWD',
      companyName: '',
    }));
  };

  const handleClose = () => {
    setOpen(false);
    setLookupKey(null);
    setForm(EMPTY_FORM(today));
  };

  const handleSubmit = async () => {
    if (!form.symbol || !form.companyName || form.quantity <= 0 || form.price <= 0) return;
    await addTransaction({ ...form, symbol: form.symbol.toUpperCase() });
    handleClose();
  };

  const symbolAdornment = isLooking ? (
    <InputAdornment position="end">
      <CircularProgress size={16} />
    </InputAdornment>
  ) : lookedUp ? (
    <InputAdornment position="end">
      <CheckCircleIcon fontSize="small" sx={{ color: 'success.main' }} />
    </InputAdornment>
  ) : lookupFailed && lookupKey ? (
    <InputAdornment position="end">
      <ErrorIcon fontSize="small" sx={{ color: 'warning.main' }} />
    </InputAdornment>
  ) : null;

  const canSubmit =
    !isLoading && form.symbol && form.companyName && form.quantity > 0 && form.price > 0;

  return (
    <>
      <Button variant="contained" startIcon={<AddIcon />} onClick={() => setOpen(true)}>
        新增交易
      </Button>

      <Dialog open={open} onClose={handleClose} maxWidth="sm" fullWidth>
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

          <Box>
            <TextField
              size="small"
              fullWidth
              label="股票代號"
              placeholder={form.market === 'TW' ? '例：2330' : '例：AAPL'}
              value={form.symbol}
              onChange={(e) => {
                setForm((f) => ({ ...f, symbol: e.target.value, companyName: '' }));
              }}
              slotProps={{ input: { endAdornment: symbolAdornment } }}
            />
            {lookedUp && (
              <Typography variant="caption" color="success.main" sx={{ mt: 0.5, display: 'block' }}>
                ✓ {lookedUp.companyName}
              </Typography>
            )}
            {lookupFailed && lookupKey && !isLooking && (
              <Typography variant="caption" color="warning.main" sx={{ mt: 0.5, display: 'block' }}>
                找不到代號，請手動輸入公司名
              </Typography>
            )}
          </Box>

          {/* Show manual company name input only when lookup failed or no lookup yet with companyName empty */}
          {(!lookedUp || lookupFailed) && (
            <TextField
              size="small"
              label="公司名稱"
              placeholder={form.market === 'TW' ? '例：台積電' : '例：Apple Inc.'}
              value={form.companyName}
              onChange={(e) => setForm((f) => ({ ...f, companyName: e.target.value }))}
            />
          )}

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
            helperText={
              lookedUp && lookedUp.currentPrice > 0
                ? `最新收盤：${form.currency} ${lookedUp.currentPrice.toLocaleString('zh-TW', { minimumFractionDigits: 2 })}`
                : undefined
            }
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
          <Button onClick={handleClose}>取消</Button>
          <Button variant="contained" onClick={handleSubmit} disabled={!canSubmit}>
            {isLoading ? '儲存中...' : '儲存'}
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
