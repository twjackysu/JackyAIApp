import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import Accordion from '@mui/material/Accordion';
import AccordionDetails from '@mui/material/AccordionDetails';
import AccordionSummary from '@mui/material/AccordionSummary';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import { liquidSharePct } from '../positionShare';
import { usePrivacy } from '../privacy';

import { groupPositions } from './positionDisplay';
import { PositionsGroupTable } from './PositionsGroupTable';

import { PositionDto } from '@/apis/portfolioApis/types';
import { fmtNum as fmt, pnlColor } from '@/utils/format';

interface Props {
  positions: PositionDto[];
  /** Denominator of the 占比 column: `PortfolioSummaryDto.liquidGrossAssetsTWD`. */
  liquidGrossAssetsTWD: number;
  onStockClick?: (p: PositionDto) => void;
}

/** The holdings, one collapsible group per kind (台股, 美股, …), each with its share and total. */
export function PositionsTable({ positions, liquidGrossAssetsTWD, onStockClick }: Props) {
  const { value, showRatio } = usePrivacy();
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});

  if (positions.length === 0) {
    return (
      <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>
        尚無持倉，點「建立持倉」開始
      </Typography>
    );
  }

  return (
    <Box>
      {groupPositions(positions).map((g) => {
        // Group keys match asset types where it matters: only REAL_ESTATE has no share.
        const groupShare = liquidSharePct(g.total, g.key, liquidGrossAssetsTWD);
        const isExpanded = expanded[g.key] ?? true;
        return (
          <Accordion
            key={g.key}
            expanded={isExpanded}
            onChange={(_, next) => setExpanded((prev) => ({ ...prev, [g.key]: next }))}
            disableGutters
          >
            <AccordionSummary expandIcon={<ExpandMoreIcon />}>
              <Box
                sx={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  width: '100%',
                  pr: 1,
                }}
              >
                <Typography variant="subtitle2" sx={{ fontWeight: 700 }}>
                  {g.label}（{g.positions.length}）
                </Typography>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
                  {showRatio && groupShare != null && (
                    <Typography variant="caption" color="text.secondary">
                      {groupShare.toFixed(1)}%
                    </Typography>
                  )}
                  <Typography
                    variant="body2"
                    sx={{ color: g.key === 'LIABILITY' ? pnlColor(g.total) : undefined }}
                  >
                    {value(fmt(g.total))}
                  </Typography>
                </Box>
              </Box>
            </AccordionSummary>
            <AccordionDetails sx={{ p: 0 }}>
              <PositionsGroupTable
                positions={g.positions}
                liquidGrossAssetsTWD={liquidGrossAssetsTWD}
                onStockClick={onStockClick}
              />
            </AccordionDetails>
          </Accordion>
        );
      })}
    </Box>
  );
}
