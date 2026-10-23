import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import { alpha } from '@mui/material/styles';
import Typography from '@mui/material/Typography';

import { drawdownBufferColor, EXPOSURE_TOOLTIP } from './exposureGauges';
import { usePrivacy } from './privacy';

import { PortfolioSummaryDto } from '@/apis/portfolioApis/types';
import { InfoPopover } from '@/components/hints';
import { fmtNum as fmt, LOSS_COLOR } from '@/utils/format';

function AllocationRow({
  label,
  value,
  pct,
  color,
}: {
  label: string;
  value: number;
  pct: number;
  color: string;
}) {
  const { value: maskValue, showRatio } = usePrivacy();
  if (value <= 0) return null;
  return (
    <Box>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 0.5 }}>
        <Typography variant="body2" sx={{ fontWeight: 600 }}>
          {label}
        </Typography>
        <Typography variant="body2" color="text.secondary">
          NT${maskValue(fmt(value))}
          {showRatio ? ` (${pct.toFixed(1)}%)` : ''}
        </Typography>
      </Box>
      <Box sx={{ height: 6, borderRadius: 3, bgcolor: alpha(color, 0.15), overflow: 'hidden' }}>
        <Box
          sx={{
            height: '100%',
            width: `${pct}%`,
            bgcolor: color,
            borderRadius: 3,
            transition: 'width 0.6s ease',
          }}
        />
      </Box>
    </Box>
  );
}

/** What the assets are, and how much market exposure they carry. */
export function AllocationCard({ summary }: { summary: PortfolioSummaryDto }) {
  const { value, showRatio } = usePrivacy();
  const {
    twStockTWD: twStock,
    usStockTWD: usStock,
    cashTWD: cash,
    fundTWD: fund,
    realEstateTWD: realEstate,
    liabilityTWD: liability,
    exposureOfLiquidAssetsPct: exposureOfLiquidAssets,
    liquidExposureMultiple: leverageMultiple,
    drawdownBufferPct: drawdownBuffer,
  } = summary;
  // Percentages are of gross assets (before subtracting debt), not net worth — otherwise
  // liability would shrink the denominator and inflate every other row's percentage. Real estate
  // is included: it's a real gross asset, just zero market exposure (see the caption below).
  const grossAssets = twStock + usStock + cash + fund + realEstate;
  const pct = (v: number) => (grossAssets > 0 ? (v / grossAssets) * 100 : 0);
  return (
    <Card
      data-tour="portfolio-tour-allocation"
      elevation={0}
      sx={{ borderRadius: 3, border: '1px solid', borderColor: 'divider', height: '100%' }}
    >
      <CardContent sx={{ p: 3 }}>
        <Typography variant="overline" color="text.secondary" sx={{ letterSpacing: 2 }}>
          資產配置
        </Typography>
        <Box sx={{ mt: 2, display: 'flex', flexDirection: 'column', gap: 1.5 }}>
          <AllocationRow label="台股" value={twStock} pct={pct(twStock)} color="#3B82F6" />
          <AllocationRow label="美股" value={usStock} pct={pct(usStock)} color="#10B981" />
          <AllocationRow label="現金" value={cash} pct={pct(cash)} color="#A78BFA" />
          <AllocationRow label="基金" value={fund} pct={pct(fund)} color="#EC4899" />
          {realEstate > 0 && (
            <AllocationRow
              label="不動產"
              value={realEstate}
              pct={pct(realEstate)}
              color="#F59E0B"
            />
          )}
        </Box>
        {liability > 0 && (
          <Box
            sx={{
              mt: 1.5,
              display: 'flex',
              justifyContent: 'space-between',
            }}
          >
            <Typography variant="body2" sx={{ fontWeight: 600, color: LOSS_COLOR }}>
              負債
            </Typography>
            <Typography variant="body2" sx={{ color: LOSS_COLOR }}>
              -NT${value(fmt(liability))}
              {showRatio ? ` (-${pct(liability).toFixed(1)}%)` : ''}
            </Typography>
          </Box>
        )}
        <Box
          sx={{
            mt: 2,
            pt: 1.5,
            borderTop: '1px dashed',
            borderColor: 'divider',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'baseline',
          }}
        >
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
            <Typography variant="body2" sx={{ fontWeight: 600 }}>
              市場曝險
            </Typography>
            {/* All three gauges below share this one numerator and differ only by denominator —
                the formulas live behind this button rather than inline, since spelling them out
                would leave more explanation than numbers on a card this size. */}
            <InfoPopover
              content={EXPOSURE_TOOLTIP}
              ariaLabel="市場曝險說明"
              iconSize={14}
              data-tour="portfolio-tour-exposure-help"
            />
          </Box>
          <Typography variant="body2" color="text.secondary">
            NT${value(fmt(summary.marketExposureTWD))}
          </Typography>
        </Box>
        {showRatio && (
          <Box
            sx={{
              display: 'flex',
              justifyContent: 'space-between',
              flexWrap: 'wrap',
              gap: 1,
              mt: 0.75,
            }}
          >
            {/* 水位 divides by gross assets, 槓桿 by net — the same numerator over two different
                denominators, so they're given different units (% vs ×) to stop them being read as
                the same kind of number sitting side by side. */}
            {exposureOfLiquidAssets != null && (
              <Typography variant="caption" color="text.secondary">
                水位{' '}
                <Box component="span" sx={{ fontWeight: 700, color: 'text.primary' }}>
                  {exposureOfLiquidAssets.toFixed(1)}%
                </Box>
              </Typography>
            )}
            <Typography variant="caption" color="text.secondary">
              槓桿{' '}
              <Box
                component="span"
                sx={{
                  fontWeight: 700,
                  // Null exactly at insolvency, where exposure ÷ equity explodes and then flips to
                  // a negative that misleadingly reads as *safer* than a large positive one.
                  color: leverageMultiple == null ? LOSS_COLOR : 'text.primary',
                }}
              >
                {leverageMultiple != null ? `${leverageMultiple.toFixed(2)}×` : '資不抵債'}
              </Box>
            </Typography>
          </Box>
        )}
        {/* The leverage multiple's reciprocal, kept alongside it because it stays monotonic and
            continuous straight through insolvency where the multiple can't. Phrased as a drawdown
            the user can feel (6% is a bad day, 20% a correction) rather than a bare ratio. */}
        {showRatio && drawdownBuffer != null && (
          <Box sx={{ display: 'flex', justifyContent: 'space-between', mt: 0.5 }}>
            <Typography variant="caption" color="text.secondary">
              抗跌空間
            </Typography>
            <Typography
              variant="caption"
              sx={{ fontWeight: 700, color: drawdownBufferColor(drawdownBuffer) }}
            >
              {drawdownBuffer > 0 ? `再跌 ${drawdownBuffer.toFixed(1)}% 歸零` : '淨值已為負'}
            </Typography>
          </Box>
        )}
        <Typography variant="caption" color="text.secondary" sx={{ mt: 1.5, display: 'block' }}>
          USD/TWD = {summary.usdTwdRate.toFixed(2)}
        </Typography>
      </CardContent>
    </Card>
  );
}
