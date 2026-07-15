import PercentIcon from '@mui/icons-material/Percent';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogContentText from '@mui/material/DialogContentText';
import DialogTitle from '@mui/material/DialogTitle';
import IconButton from '@mui/material/IconButton';
import TextField from '@mui/material/TextField';
import Tooltip from '@mui/material/Tooltip';
import { useEffect, useState } from 'react';

import {
  useGetPortfolioSettingsQuery,
  useSavePortfolioSettingsMutation,
} from '@/apis/portfolioApis';

/** Rate is stored as a 0–1 multiplier (0.28 = 2.8折); the input works in the familiar 0–10
 * "折" scale, so the two need converting at the read/write boundary. */
const rateToZhe = (rate: number) => Math.round(rate * 10 * 100) / 100;
const zheToRate = (zhe: number) => zhe / 10;

export function FeeSettingsDialog() {
  const [open, setOpen] = useState(false);
  const { data } = useGetPortfolioSettingsQuery();
  const [saveSettings, { isLoading: isSaving }] = useSavePortfolioSettingsMutation();
  const [zhe, setZhe] = useState('2.8');

  useEffect(() => {
    if (data?.data) setZhe(String(rateToZhe(data.data.feeDiscountRate)));
  }, [data]);

  const handleSave = async () => {
    const parsed = parseFloat(zhe);
    if (!Number.isFinite(parsed) || parsed <= 0) return;
    await saveSettings({ feeDiscountRate: zheToRate(parsed) }).unwrap();
    setOpen(false);
  };

  return (
    <>
      <Tooltip title="台股手續費折扣設定">
        <IconButton onClick={() => setOpen(true)} size="small">
          <PercentIcon />
        </IconButton>
      </Tooltip>
      <Dialog open={open} onClose={() => setOpen(false)} maxWidth="xs" fullWidth>
        <DialogTitle>台股手續費折扣</DialogTitle>
        <DialogContent>
          <DialogContentText sx={{ mb: 2 }}>
            用來估計台股賣出時的手續費（牌告 0.1425% × 折扣）與證交稅（個股 0.3%、ETF 0.1%、債券 ETF
            免徵），從未實現損益中扣除，更接近券商實際會顯示的數字。美股與基金不受影響。
          </DialogContentText>
          <TextField
            label="手續費折扣（幾折）"
            type="number"
            value={zhe}
            onChange={(e) => setZhe(e.target.value)}
            fullWidth
            slotProps={{ htmlInput: { min: 0.1, max: 10, step: 0.1 } }}
            helperText="例：2.8 折 = 牌告費率的 28%"
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpen(false)}>取消</Button>
          <Button variant="contained" onClick={handleSave} disabled={isSaving}>
            儲存
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
