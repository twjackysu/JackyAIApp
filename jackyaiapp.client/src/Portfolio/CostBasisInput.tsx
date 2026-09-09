import AddShoppingCartIcon from '@mui/icons-material/AddShoppingCart';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Popover from '@mui/material/Popover';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import { fmtNum } from '@/utils/format';

interface Props {
  quantity: number;
  costBasis: number | null;
  currency: string;
  /** 「股」for stocks, 「單位」for funds — used in labels/helper text. */
  unitLabel: string;
  qtyStep: number;
  onChange: (patch: { quantity?: number; costBasis?: number | null }) => void;
}

/**
 * Total-cost input with a live per-unit average and an "加碼" helper that
 * weight-averages a new lot into the existing position automatically.
 */
export function CostBasisInput({
  quantity,
  costBasis,
  currency,
  unitLabel,
  qtyStep,
  onChange,
}: Props) {
  const [anchor, setAnchor] = useState<HTMLButtonElement | null>(null);
  const [addQty, setAddQty] = useState('');
  const [addPrice, setAddPrice] = useState('');

  const avg = costBasis != null && quantity > 0 ? costBasis / quantity : null;

  const closePopover = () => {
    setAnchor(null);
    setAddQty('');
    setAddPrice('');
  };

  const applyLot = () => {
    const n = parseFloat(addQty);
    const price = parseFloat(addPrice);
    if (!(n > 0) || !(price > 0)) return;
    // Weighted average: append the new lot's quantity and its cost (n × price).
    onChange({
      quantity: quantity + n,
      costBasis: (costBasis ?? 0) + n * price,
    });
    closePopover();
  };

  return (
    <Box sx={{ width: 150 }}>
      <TextField
        size="small"
        type="number"
        label="總成本(選填)"
        placeholder="留空不算損益"
        value={costBasis ?? ''}
        onChange={(e) =>
          onChange({ costBasis: e.target.value === '' ? null : parseFloat(e.target.value) })
        }
        slotProps={{ htmlInput: { min: 0, step: 0.01 } }}
        fullWidth
      />
      <Box
        sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mt: 0.25 }}
      >
        <Typography variant="caption" color="text.secondary" sx={{ fontSize: 11 }}>
          {avg != null ? `每${unitLabel} ${currency} ${fmtNum(avg, 2)}` : ' '}
        </Typography>
        <Button
          size="small"
          startIcon={<AddShoppingCartIcon sx={{ fontSize: 14 }} />}
          onClick={(e) => setAnchor(e.currentTarget)}
          sx={{ minWidth: 0, px: 0.5, fontSize: 11 }}
        >
          加碼
        </Button>
      </Box>

      <Popover
        open={!!anchor}
        anchorEl={anchor}
        onClose={closePopover}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        transformOrigin={{ vertical: 'top', horizontal: 'right' }}
        // Sit above the Dialog modal layer.
        slotProps={{ paper: { sx: { p: 2, width: 240, zIndex: (t) => t.zIndex.modal + 1 } } }}
      >
        <Typography variant="subtitle2" sx={{ fontWeight: 700, mb: 1 }}>
          加碼攤平
        </Typography>
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
          <TextField
            size="small"
            type="number"
            label={`加碼${unitLabel}數`}
            value={addQty}
            onChange={(e) => setAddQty(e.target.value)}
            slotProps={{ htmlInput: { min: 0, step: qtyStep } }}
            autoFocus
          />
          <TextField
            size="small"
            type="number"
            label={`成交價 (每${unitLabel})`}
            value={addPrice}
            onChange={(e) => setAddPrice(e.target.value)}
            slotProps={{ htmlInput: { min: 0, step: 0.0001 } }}
          />
          <Typography variant="caption" color="text.secondary">
            自動加入單位數，總成本 +{' '}
            {addQty && addPrice && parseFloat(addQty) > 0 && parseFloat(addPrice) > 0
              ? `${currency} ${fmtNum(parseFloat(addQty) * parseFloat(addPrice), 2)}`
              : '—'}
          </Typography>
          <Box sx={{ display: 'flex', justifyContent: 'flex-end', gap: 1 }}>
            <Button size="small" onClick={closePopover}>
              取消
            </Button>
            <Button
              size="small"
              variant="contained"
              onClick={applyLot}
              disabled={!(parseFloat(addQty) > 0 && parseFloat(addPrice) > 0)}
            >
              套用
            </Button>
          </Box>
        </Box>
      </Popover>
    </Box>
  );
}
