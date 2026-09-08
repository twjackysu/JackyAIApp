import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Grid from '@mui/material/Grid';
import { alpha } from '@mui/material/styles';
import Typography from '@mui/material/Typography';

import { computeConcentration } from './concentration';
import { usePrivacy } from './privacy';
import { StatBlock } from './StatBlock';

import { PositionDto } from '@/apis/portfolioApis/types';

interface ConcentrationCardProps {
  positions: PositionDto[];
}

const TOP_N = 5;

/** Pure client-side arithmetic over PortfolioSummaryDto.positions — no backend call, no AI,
 * instant. Position-level concentration risk only: cash/liability/real estate are excluded, since
 * a house or a debt isn't "stock-picking risk" the way an oversized single security is. */
export function ConcentrationCard({ positions }: ConcentrationCardProps) {
  const { showRatio } = usePrivacy();

  const result = computeConcentration(positions);
  if (!result) {
    return (
      <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>
        尚無股票/基金持倉，無法計算集中度
      </Typography>
    );
  }
  const { sorted, shares, hhi, top1Pct, top3Pct, top5Pct, level } = result;

  return (
    <Box>
      <Grid container spacing={2} sx={{ mb: 2 }}>
        <Grid size={{ xs: 6, sm: 3 }}>
          <StatBlock label="最大持股佔比" value={showRatio ? `${top1Pct.toFixed(1)}%` : '—'} />
        </Grid>
        <Grid size={{ xs: 6, sm: 3 }}>
          <StatBlock label="前3大佔比" value={showRatio ? `${top3Pct.toFixed(1)}%` : '—'} />
        </Grid>
        <Grid size={{ xs: 6, sm: 3 }}>
          <StatBlock label="前5大佔比" value={showRatio ? `${top5Pct.toFixed(1)}%` : '—'} />
        </Grid>
        <Grid size={{ xs: 6, sm: 3 }}>
          <StatBlock label="集中度" value="—">
            {showRatio ? (
              <Chip
                label={`${level.label} (HHI ${hhi.toFixed(0)})`}
                size="small"
                sx={{
                  mt: 0.5,
                  bgcolor: alpha(level.color, 0.15),
                  color: level.color,
                  fontWeight: 700,
                  border: 'none',
                }}
              />
            ) : null}
          </StatBlock>
        </Grid>
      </Grid>

      {showRatio && (
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
          {sorted.slice(0, TOP_N).map((p, i) => (
            <Box
              key={`${p.assetType}-${p.symbol}-${p.name}`}
              sx={{ display: 'flex', alignItems: 'center', gap: 1 }}
            >
              <Typography variant="body2" sx={{ width: 90, flexShrink: 0 }} noWrap>
                {p.name || p.symbol}
              </Typography>
              <Box
                sx={{
                  flex: 1,
                  height: 8,
                  borderRadius: 4,
                  bgcolor: alpha('#3B82F6', 0.15),
                  overflow: 'hidden',
                }}
              >
                <Box
                  sx={{
                    height: '100%',
                    width: `${shares[i]}%`,
                    bgcolor: '#3B82F6',
                    borderRadius: 4,
                  }}
                />
              </Box>
              <Typography
                variant="body2"
                color="text.secondary"
                sx={{ width: 48, textAlign: 'right', flexShrink: 0 }}
              >
                {shares[i].toFixed(1)}%
              </Typography>
            </Box>
          ))}
        </Box>
      )}

      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 2 }}>
        僅計算股票與基金持倉（不含現金、負債、不動產）。HHI（Herfindahl-Hirschman Index）&lt;1500
        為分散，1500–2500 為中度集中，&gt;2500 為高度集中。
      </Typography>
    </Box>
  );
}
