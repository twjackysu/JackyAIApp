import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Skeleton from '@mui/material/Skeleton';
import Typography from '@mui/material/Typography';

import {
  Advice,
  advise,
  BrakeParams,
  BrakeSettings,
  PROFILES,
  resolveParams,
  StrategyPrices,
} from '../drawdownBrake';
import {
  aboveTargetNotice,
  adviceMessage,
  cappedNotice,
  coreFactorAnomalyMessage,
  describeComposition,
  describeReconciliation,
  fmtMultiple,
  NO_SCOPE_ASSETS,
  priceFailedMessage,
  STATE_LABEL,
} from '../drawdownBrakeCopy';
import {
  extractStrategyPositions,
  findSplitGuardHit,
  isSnapshotStale,
  splitGuardMessage,
  STALE_SNAPSHOT_MESSAGE,
} from '../drawdownBrakeHoldings';
import {
  classifyPositions,
  CORE_FACTORS,
  ScopeClassification,
  toScopeHoldings,
} from '../drawdownBrakeScope';
import { usePrivacy } from '../privacy';

import { ActiveSignal } from './activeSignal';
import { BucketList } from './BucketList';
import { NoAdviceReason, noAdviceReason } from './noAdviceReason';
import { useStrategyPrices } from './useStrategyPrices';

import { PortfolioSummaryDto } from '@/apis/portfolioApis/types';
import { todayISO } from '@/utils/date';
import { fmtNum } from '@/utils/format';

interface NoAdviceNoticeProps {
  reason: NoAdviceReason;
  active: ActiveSignal;
  params: BrakeParams;
  profileLabel: string;
}

/** 暫時不給建議時，說明原因。 */
function NoAdviceNotice({ reason, active, params, profileLabel }: NoAdviceNoticeProps) {
  switch (reason.kind) {
    case 'splitGuard':
      return (
        <>
          <Typography variant="body1" sx={{ mb: 1.5, overflowWrap: 'anywhere' }}>
            {`目前狀態：${STATE_LABEL[active.state]}。依『${profileLabel}』等級，整體有效曝險的目標是 ${fmtMultiple(active.state === 'OFFENSE' ? params.eOn : params.eOff)} 倍。`}
          </Typography>
          <Alert severity="error" sx={{ mb: 1.5 }}>
            {splitGuardMessage(reason.hit)}
          </Alert>
        </>
      );
    case 'noScopeAssets':
      return (
        <Alert severity="info" sx={{ mb: 1.5 }}>
          {NO_SCOPE_ASSETS}
        </Alert>
      );
    case 'unusableHoldings':
      return (
        <>
          {reason.priceFailed.length > 0 && (
            <Alert severity="warning" sx={{ mb: 1.5 }}>
              <Typography variant="body2" sx={{ fontWeight: 600 }}>
                {priceFailedMessage(reason.priceFailed.length)}
              </Typography>
              <Typography variant="caption" sx={{ overflowWrap: 'anywhere' }}>
                {reason.priceFailed.map((p) => p.symbol).join('、')}
              </Typography>
            </Alert>
          )}
          {reason.coreFactorAnomaly != null && (
            <Alert severity="warning" sx={{ mb: 1.5 }}>
              {coreFactorAnomalyMessage(
                reason.coreFactorAnomaly,
                CORE_FACTORS[reason.coreFactorAnomaly],
              )}
            </Alert>
          )}
        </>
      );
    case 'priceLoading':
      return <Skeleton variant="rounded" height={64} />;
    case 'priceMissing':
      return (
        <Alert severity="warning" sx={{ mb: 1.5 }}>
          {`無法取得 ${reason.symbol} 的最新價格，暫時無法計算建議股數。`}
        </Alert>
      );
  }
}

interface AdviceDetailsProps {
  summary: PortfolioSummaryDto;
  scope: ScopeClassification;
  advice: Advice;
  prices: StrategyPrices;
  active: ActiveSignal;
  profileLabel: string;
}

/** 建議：結論、說明、標示、對帳行與分桶表。 */
function AdviceDetails({
  summary,
  scope,
  advice,
  prices,
  active,
  profileLabel,
}: AdviceDetailsProps) {
  const { value, showRatio } = usePrivacy();

  const reconciliation =
    scope.excluded.length > 0
      ? describeReconciliation({
          liquidGross: `NT$${value(fmtNum(summary.liquidGrossAssetsTWD))}`,
          waterPct: showRatio ? summary.exposureOfLiquidAssetsPct : null,
          excludedSymbols: [...new Set(scope.excluded.map((p) => p.symbol))],
          excludedValue: `NT$${value(fmtNum(scope.excludedValue))}`,
          scopeAssets: `NT$${value(fmtNum(scope.scopeAssets))}`,
          eActual: advice.eActual,
        })
      : null;

  return (
    <Box
      sx={{
        border: '1px solid',
        borderColor: 'divider',
        borderRadius: 2,
        p: 2,
        mb: 1.5,
        minWidth: 0,
      }}
    >
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap', mb: 1 }}>
        <Chip
          size="small"
          label={advice.action}
          color={
            advice.action === '不動' ? 'default' : advice.action === '減碼' ? 'warning' : 'success'
          }
        />
        <Typography variant="caption" color="text.secondary">
          依『{profileLabel}』等級
        </Typography>
      </Box>
      <Typography variant="body1" sx={{ overflowWrap: 'anywhere' }}>
        {adviceMessage({
          advice,
          state: active.state,
          drawdownPct: active.drawdownPct,
          distanceToTriggerPct: active.distanceToTriggerPct,
          profileLabel,
        })}
      </Typography>

      {advice.aboveTarget && (
        <Alert severity="warning" sx={{ mt: 1.5 }}>
          {aboveTargetNotice(advice.eReach, advice.eTarget)}
        </Alert>
      )}
      {advice.capped && (
        <Alert severity="warning" sx={{ mt: 1.5 }}>
          {cappedNotice(advice.eReach, advice.eTarget, advice.othersShare > 0)}
        </Alert>
      )}

      {reconciliation && (
        <Typography
          variant="caption"
          color="text.secondary"
          sx={{ display: 'block', mt: 1.5, overflowWrap: 'anywhere' }}
        >
          {reconciliation}
        </Typography>
      )}

      <BucketList scope={scope} advice={advice} prices={prices} />
      <Typography
        variant="caption"
        color="text.secondary"
        sx={{ display: 'block', mt: 1, overflowWrap: 'anywhere' }}
      >
        {`目標配置：${describeComposition(advice.weights, advice.othersShare)}｜有效曝險 ${fmtMultiple(advice.eActual)} 倍（目標 ${fmtMultiple(advice.eTarget)} 倍）｜策略範圍資產 NT$${value(fmtNum(advice.scopeAssets))}`}
      </Typography>
    </Box>
  );
}

interface AdvicePanelProps {
  summary: PortfolioSummaryDto;
  settings: BrakeSettings;
  active: ActiveSignal;
}

/** 依目前狀態與持股算出的建議；有任何疑慮時（見 `noAdviceReason`）只說明原因，不給股數。 */
export function AdvicePanel({ summary, settings, active }: AdvicePanelProps) {
  const params = resolveParams(settings);
  const profileLabel = PROFILES[settings.profile].label;

  const scope = classifyPositions(summary.positions);
  const held = extractStrategyPositions(summary.positions);
  const { prices, loading, missing } = useStrategyPrices(held);

  const reason = noAdviceReason({
    guardHit: findSplitGuardHit(held, summary.effectiveDate),
    scope,
    priceLoading: loading,
    missingPrice: missing,
  });
  const advice = reason
    ? null
    : advise(active.prevState, active.state, params, toScopeHoldings(scope), prices);

  return (
    <Box sx={{ mb: 2 }}>
      {isSnapshotStale(summary.effectiveDate, todayISO()) && (
        <Alert severity="warning" sx={{ mb: 1.5 }}>
          {STALE_SNAPSHOT_MESSAGE}
        </Alert>
      )}
      {reason && (
        <NoAdviceNotice
          reason={reason}
          active={active}
          params={params}
          profileLabel={profileLabel}
        />
      )}
      {advice && (
        <AdviceDetails
          summary={summary}
          scope={scope}
          advice={advice}
          prices={prices}
          active={active}
          profileLabel={profileLabel}
        />
      )}
    </Box>
  );
}
