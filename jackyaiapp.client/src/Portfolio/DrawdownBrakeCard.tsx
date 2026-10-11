import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import Accordion from '@mui/material/Accordion';
import AccordionDetails from '@mui/material/AccordionDetails';
import AccordionSummary from '@mui/material/AccordionSummary';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Collapse from '@mui/material/Collapse';
import Skeleton from '@mui/material/Skeleton';
import TextField from '@mui/material/TextField';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';
import Typography from '@mui/material/Typography';
import { ReactNode, useState } from 'react';

import { ChartSectionCard } from './ChartSectionCard';
import {
  Advice,
  advise,
  BrakeSettings,
  BrakeState,
  DD_TRIGGER_PCT,
  DEFENSE_E_OPTIONS,
  loadBrakeSettings,
  MIN_CASH_RATIO_MAX,
  MIN_CASH_RATIO_MIN,
  PROFILE_KEYS,
  PROFILES,
  ProfileKey,
  resolveParams,
  saveBrakeSettings,
  StrategyPrices,
  StrategySymbol,
} from './drawdownBrake';
import {
  aboveTargetNotice,
  adviceMessage,
  BACKTEST_NEXT_DAY,
  BACKTEST_PERIOD,
  BACKTEST_SCOPE_NOTE,
  cappedNotice,
  CASH_BUCKET_NOTE,
  coreFactorAnomalyMessage,
  describeCashRowAdvice,
  describeComposition,
  describeCoreRowAdvice,
  describeLev2RowAdvice,
  describeReconciliation,
  DISCLAIMER,
  fmtMultiple,
  LEV2_ROW_TITLE,
  NO_SCOPE_ASSETS,
  OTHERS_ADVICE,
  OTHERS_REMINDER,
  priceFailedMessage,
  RISK_POINTS,
  SIMULATED_NOTE,
  STATE_LABEL,
} from './drawdownBrakeCopy';
import {
  extractStrategyPositions,
  findSplitGuardHit,
  isSnapshotStale,
  priceRefreshSymbols,
  requiredPriceSymbols,
  splitGuardMessage,
  STALE_SNAPSHOT_MESSAGE,
} from './drawdownBrakeHoldings';
import { DrawdownBrakeFreshness } from './DrawdownBrakeRefresh';
import {
  BucketTotals,
  classifyPositions,
  CORE_FACTORS,
  CORE_LEVERAGED_2X,
  CORE_SYMBOLS,
  ScopeClassification,
  toScopeHoldings,
} from './drawdownBrakeScope';
import { usePrivacy } from './privacy';

import { useGetUserInfoQuery } from '@/apis/accountApis';
import { useGetDrawdownBrakeQuery, useLookupStockQuery } from '@/apis/portfolioApis';
import { DrawdownBrakeDto, PortfolioSummaryDto } from '@/apis/portfolioApis/types';
import { getApiErrorMessage } from '@/apis/utils/errorMessage';
import { todayISO } from '@/utils/date';
import { fmtNum, fmtQty } from '@/utils/format';

function isValidPrice(price: number | null | undefined): price is number {
  return typeof price === 'number' && Number.isFinite(price) && price > 0;
}

/** 持有且快照有價格 → 用快照價（已含交易時段的即時價）；否則（沒持有，或價格抓取失敗）查價。
 * 沒持有 0050 / 00631L 的使用者（PM 假設 A4）也要有價格才能換算建議股數。`required` 為 false（這檔
 * 不會用到，見 `requiredPriceSymbols`）時不查價。 */
function useStrategyPrice(symbol: StrategySymbol, summaryPrice: number | null, required: boolean) {
  const needLookup = required && summaryPrice == null;
  const { data, isLoading } = useLookupStockQuery({ market: 'TW', symbol }, { skip: !needLookup });
  const lookedUp = data?.data.currentPrice;
  const price = summaryPrice ?? (isValidPrice(lookedUp) ? lookedUp : null);
  return { price, loading: needLookup && isLoading };
}

function formatEstimateTime(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return date.toLocaleString('zh-TW', {
    hour12: false,
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
}

function StatItem({ label, value }: { label: string; value: string }) {
  return (
    <Box sx={{ minWidth: 0 }}>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
        {label}
      </Typography>
      <Typography variant="body2" sx={{ fontWeight: 600, overflowWrap: 'anywhere' }}>
        {value}
      </Typography>
    </Box>
  );
}

/** 固定警語：卡片所有狀態（載入中、資料準備中、資料未更新、錯誤、預估）都在，不可關閉。 */
function Disclaimer() {
  return (
    <Alert severity="warning" variant="outlined" role="note" sx={{ mb: 2 }}>
      <Typography variant="body2" sx={{ fontWeight: 600, overflowWrap: 'anywhere' }}>
        {DISCLAIMER}
      </Typography>
      <Typography variant="caption" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>
        {SIMULATED_NOTE}
      </Typography>
    </Alert>
  );
}

interface ActiveSignal {
  state: BrakeState;
  prevState: BrakeState | null;
  drawdownPct: number;
  distanceToTriggerPct: number;
  isEstimate: boolean;
}

/** 有預估時以預估狀態計算；沒有（第一階段恆為如此）就用收盤後的正式訊號。資料準備中則為 null。 */
function pickActiveSignal(signal: DrawdownBrakeDto): ActiveSignal | null {
  if (signal.isInitializing) return null;
  const { estimate } = signal;
  if (estimate) {
    return {
      state: estimate.state,
      prevState: estimate.prevState,
      drawdownPct: estimate.drawdownPct,
      // 預估沒有單獨的 distance 欄位：drawdownPct 距 -8% 觸發線的空間。
      distanceToTriggerPct: estimate.drawdownPct + DD_TRIGGER_PCT,
      isEstimate: true,
    };
  }
  if (signal.state == null || signal.drawdownPct == null || signal.distanceToTriggerPct == null) {
    return null;
  }
  return {
    state: signal.state,
    prevState: signal.prevState,
    drawdownPct: signal.drawdownPct,
    distanceToTriggerPct: signal.distanceToTriggerPct,
    isEstimate: false,
  };
}

function SignalSummary({ signal, active }: { signal: DrawdownBrakeDto; active: ActiveSignal }) {
  const distance = active.distanceToTriggerPct;
  const estimate = signal.estimate;
  return (
    <Box sx={{ mb: 2 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: 1, mb: 1.5 }}>
        <Typography variant="body2" color="text.secondary">
          目前狀態
        </Typography>
        <Chip
          size="small"
          label={STATE_LABEL[active.state]}
          color={active.state === 'OFFENSE' ? 'success' : 'warning'}
        />
        {active.isEstimate && (
          <Chip size="small" variant="outlined" color="info" label="預估訊號（盤中）" />
        )}
      </Box>
      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(140px, 1fr))',
          gap: 1.5,
        }}
      >
        <StatItem label="資料截止日" value={signal.asOfDate ?? '—'} />
        <StatItem label="距近一年高點" value={`${active.drawdownPct.toFixed(1)}%`} />
        <StatItem
          label="距煞車觸發點"
          value={
            distance >= 0 ? `${distance.toFixed(1)}%` : `已低於 ${Math.abs(distance).toFixed(1)}%`
          }
        />
        <StatItem label="最近切換日" value={signal.lastSwitchDate ?? '—'} />
      </Box>
      {estimate && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
          盤中預估（{formatEstimateTime(estimate.asOfTime)}），收盤後以正式數據為準。
        </Typography>
      )}
    </Box>
  );
}

interface AdvicePanelProps {
  summary: PortfolioSummaryDto;
  settings: BrakeSettings;
  active: ActiveSignal;
}

interface BucketRowProps {
  title: string;
  bucket: BucketTotals;
  scopeAssets: number;
  /** 核心與現金類才有目標市值。 */
  target?: number;
  /** 建議欄；其他持股固定為「不動（由您自行決定）」。 */
  adviceText: string;
  /** 列內附屬的內容（台股正 2 類的各檔明細）。 */
  children?: ReactNode;
}

/** 分桶表的一列。手機寬度下不用寬表格，改成堆疊式：標題一行，數字在可換行的格子裡，建議獨占一行。 */
function BucketRow({ title, bucket, scopeAssets, target, adviceText, children }: BucketRowProps) {
  const { value } = usePrivacy();
  return (
    <Box
      component="li"
      sx={{
        listStyle: 'none',
        border: '1px solid',
        borderColor: 'divider',
        borderRadius: 2,
        p: 1.5,
        minWidth: 0,
      }}
    >
      <Typography variant="body2" sx={{ fontWeight: 700, mb: 0.5 }}>
        {title}
      </Typography>
      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(120px, 1fr))',
          gap: 1,
        }}
      >
        <StatItem label="目前市值" value={`NT$${value(fmtNum(bucket.value))}`} />
        <StatItem
          label="目前曝險貢獻"
          value={`${fmtMultiple(scopeAssets > 0 ? bucket.exposure / scopeAssets : 0)} 倍`}
        />
        {target != null && <StatItem label="目標市值" value={`NT$${value(fmtNum(target))}`} />}
      </Box>
      <Typography variant="body2" sx={{ fontWeight: 600, mt: 0.75, overflowWrap: 'anywhere' }}>
        {`建議：${adviceText}`}
      </Typography>
      {children}
    </Box>
  );
}

interface Lev2MemberRowsProps {
  scope: ScopeClassification;
  advice: Advice;
  prices: StrategyPrices;
}

/** 台股正 2 類之下，縮排列出各檔的現值與建議股數（只列有持有或有建議的）。類別列給整組一個結論與金額，
 * 股數怎麼分到各檔由 `splitLev2Order` 決定。 */
function Lev2MemberRows({ scope, advice, prices }: Lev2MemberRowsProps) {
  const { value } = usePrivacy();
  const fmtAmount = (n: number) => value(fmtNum(n));
  const members = CORE_LEVERAGED_2X.flatMap((symbol) => {
    const holding = scope.lev2Holdings.find((h) => h.symbol === symbol);
    const order = advice.orders.find((o) => o.symbol === symbol);
    return holding || order ? [{ symbol, holding, order }] : [];
  });
  if (members.length === 0) return null;

  return (
    <Box
      component="ul"
      aria-label={`${LEV2_ROW_TITLE}各檔`}
      sx={{ m: 0, mt: 1, p: 0, display: 'grid', gap: 1 }}
    >
      {members.map(({ symbol, holding, order }) => (
        <Box
          component="li"
          key={symbol}
          sx={{
            listStyle: 'none',
            minWidth: 0,
            pl: 1.5,
            borderLeft: '2px solid',
            borderColor: 'divider',
          }}
        >
          <Typography variant="body2" sx={{ fontWeight: 700 }}>
            {symbol}
          </Typography>
          <StatItem label="目前市值" value={`NT$${fmtAmount(holding?.value ?? 0)}`} />
          <Typography variant="body2" sx={{ fontWeight: 600, mt: 0.5, overflowWrap: 'anywhere' }}>
            {`建議：${describeCoreRowAdvice(order, prices[symbol] ?? 0, fmtAmount)}`}
          </Typography>
        </Box>
      ))}
    </Box>
  );
}

interface BucketListProps {
  scope: ScopeClassification;
  advice: Advice;
  prices: StrategyPrices;
}

/** 12b 分桶表：0050、台股正 2（00631L／00685L，其下列各檔）、其他一倍商品、其他倍數商品（有才顯示）、
 * 現金類。沒有的桶不顯示。 */
function BucketList({ scope, advice, prices }: BucketListProps) {
  const { value } = usePrivacy();
  const fmtAmount = (n: number) => value(fmtNum(n));
  const orderOf = (symbol: StrategySymbol) => advice.orders.find((o) => o.symbol === symbol);
  const { scopeAssets, targets } = advice;

  const showCore = (bucket: BucketTotals, target: number) => bucket.count > 0 || target > 0;
  const hasOthers = scope.otherOne.count > 0 || scope.otherMulti.count > 0;

  return (
    <>
      <Box
        component="ul"
        aria-label="分桶配置與建議"
        sx={{ m: 0, mt: 1.5, p: 0, display: 'grid', gap: 1 }}
      >
        {showCore(scope.core0050, targets['0050']) && (
          <BucketRow
            title="0050"
            bucket={scope.core0050}
            scopeAssets={scopeAssets}
            target={targets['0050']}
            adviceText={describeCoreRowAdvice(orderOf('0050'), prices['0050'] ?? 0, fmtAmount)}
          />
        )}
        {showCore(scope.coreLev2, targets.lev2) && (
          <BucketRow
            title={LEV2_ROW_TITLE}
            bucket={scope.coreLev2}
            scopeAssets={scopeAssets}
            target={targets.lev2}
            adviceText={describeLev2RowAdvice(advice.lev2Change, fmtAmount)}
          >
            <Lev2MemberRows scope={scope} advice={advice} prices={prices} />
          </BucketRow>
        )}
        {scope.otherOne.count > 0 && (
          <BucketRow
            title="其他一倍商品"
            bucket={scope.otherOne}
            scopeAssets={scopeAssets}
            adviceText={OTHERS_ADVICE}
          />
        )}
        {scope.otherMulti.count > 0 && (
          <BucketRow
            title="其他倍數商品"
            bucket={scope.otherMulti}
            scopeAssets={scopeAssets}
            adviceText={OTHERS_ADVICE}
          />
        )}
        {showCore(scope.cash, targets.cash) && (
          <BucketRow
            title="現金類"
            bucket={scope.cash}
            scopeAssets={scopeAssets}
            target={targets.cash}
            adviceText={describeCashRowAdvice(advice.cashChange, fmtAmount)}
          />
        )}
      </Box>
      {hasOthers && (
        <Typography
          variant="caption"
          color="text.secondary"
          sx={{ display: 'block', mt: 1, overflowWrap: 'anywhere' }}
        >
          {OTHERS_REMINDER}
        </Typography>
      )}
      {(scope.cash.count > 0 || targets.cash > 0) && (
        <Typography
          variant="caption"
          color="text.secondary"
          sx={{ display: 'block', mt: 0.5, overflowWrap: 'anywhere' }}
        >
          {CASH_BUCKET_NOTE}
        </Typography>
      )}
    </>
  );
}

function AdvicePanel({ summary, settings, active }: AdvicePanelProps) {
  const { value, showRatio } = usePrivacy();
  const params = resolveParams(settings);
  const profileLabel = PROFILES[settings.profile].label;

  const scope = classifyPositions(summary.positions);
  const held = extractStrategyPositions(summary.positions);
  const heldBySymbol = Object.fromEntries(held.map((h) => [h.symbol, h])) as Partial<
    Record<StrategySymbol, (typeof held)[number]>
  >;
  // 只有算得到股數用得上的標的才需要價格（見 `requiredPriceSymbols`）。hooks 不能放進迴圈，所以每個核心
  // 標的各呼叫一次；`Record<StrategySymbol, …>` 讓日後在 `CORE_LEVERAGED_2X` 增加成員時這裡編譯失敗。
  const required = requiredPriceSymbols(held);
  const priceInfo: Record<StrategySymbol, ReturnType<typeof useStrategyPrice>> = {
    '0050': useStrategyPrice(
      '0050',
      heldBySymbol['0050']?.price ?? null,
      required.includes('0050'),
    ),
    '00631L': useStrategyPrice(
      '00631L',
      heldBySymbol['00631L']?.price ?? null,
      required.includes('00631L'),
    ),
    '00685L': useStrategyPrice(
      '00685L',
      heldBySymbol['00685L']?.price ?? null,
      required.includes('00685L'),
    ),
  };

  const guardHit = findSplitGuardHit(held, summary.effectiveDate);
  const snapshotStale = isSnapshotStale(summary.effectiveDate, todayISO());

  const priceLoading = required.some((s) => priceInfo[s].loading);
  const missingPrice = required.find((s) => priceInfo[s].price == null && !priceInfo[s].loading);
  const prices: StrategyPrices = Object.fromEntries(
    required.flatMap((s) => {
      const price = priceInfo[s].price;
      return price == null ? [] : [[s, price]];
    }),
  );

  // 依序擋下：分割疑慮 → 沒有流動資產 → 範圍內有價格失敗的持股 → 核心倍數異常 → 查價中／查不到價。
  // 寧可暫時沒有建議，也不給錯的股數。
  const hasScopeAssets = scope.scopeAssets > 0;
  const blocked =
    !!guardHit ||
    !hasScopeAssets ||
    scope.priceFailed.length > 0 ||
    scope.coreFactorAnomaly != null ||
    priceLoading ||
    !!missingPrice;
  const advice = !blocked
    ? advise(active.prevState, active.state, params, toScopeHoldings(scope), prices)
    : null;

  const reconciliation =
    advice && scope.excluded.length > 0
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
    <Box sx={{ mb: 2 }}>
      {snapshotStale && (
        <Alert severity="warning" sx={{ mb: 1.5 }}>
          {STALE_SNAPSHOT_MESSAGE}
        </Alert>
      )}

      {guardHit && (
        <>
          <Typography variant="body1" sx={{ mb: 1.5, overflowWrap: 'anywhere' }}>
            {`目前狀態：${STATE_LABEL[active.state]}。依『${profileLabel}』等級，整體有效曝險的目標是 ${fmtMultiple(active.state === 'OFFENSE' ? params.eOn : params.eOff)} 倍。`}
          </Typography>
          <Alert severity="error" sx={{ mb: 1.5 }}>
            {splitGuardMessage(guardHit)}
          </Alert>
        </>
      )}

      {!guardHit && !hasScopeAssets && (
        <Alert severity="info" sx={{ mb: 1.5 }}>
          {NO_SCOPE_ASSETS}
        </Alert>
      )}

      {!guardHit && hasScopeAssets && scope.priceFailed.length > 0 && (
        <Alert severity="warning" sx={{ mb: 1.5 }}>
          <Typography variant="body2" sx={{ fontWeight: 600 }}>
            {priceFailedMessage(scope.priceFailed.length)}
          </Typography>
          <Typography variant="caption" sx={{ overflowWrap: 'anywhere' }}>
            {scope.priceFailed.map((p) => p.symbol).join('、')}
          </Typography>
        </Alert>
      )}

      {!guardHit && hasScopeAssets && scope.coreFactorAnomaly != null && (
        <Alert severity="warning" sx={{ mb: 1.5 }}>
          {coreFactorAnomalyMessage(scope.coreFactorAnomaly, CORE_FACTORS[scope.coreFactorAnomaly])}
        </Alert>
      )}

      {!guardHit &&
        hasScopeAssets &&
        scope.priceFailed.length === 0 &&
        scope.coreFactorAnomaly == null &&
        priceLoading && <Skeleton variant="rounded" height={64} />}

      {!guardHit &&
        hasScopeAssets &&
        scope.priceFailed.length === 0 &&
        scope.coreFactorAnomaly == null &&
        !priceLoading &&
        missingPrice && (
          <Alert severity="warning" sx={{ mb: 1.5 }}>
            {`無法取得 ${missingPrice} 的最新價格，暫時無法計算建議股數。`}
          </Alert>
        )}

      {advice && (
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
                advice.action === '不動'
                  ? 'default'
                  : advice.action === '減碼'
                    ? 'warning'
                    : 'success'
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
      )}
    </Box>
  );
}

/** 持股資料來源與日期 — 建議股數是依這份快照算的，使用者要能看出它有多新。 */
function HoldingsInfo({ summary }: { summary: PortfolioSummaryDto }) {
  const held = extractStrategyPositions(summary.positions);
  const bySymbol = Object.fromEntries(held.map((h) => [h.symbol, h]));
  return (
    <Box sx={{ mb: 2 }}>
      <Typography variant="body2" sx={{ fontWeight: 600 }}>
        持股資料日期：{summary.effectiveDate ?? '—'}
      </Typography>
      <Typography variant="caption" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>
        {CORE_SYMBOLS.map(
          (symbol) =>
            `${symbol} ${bySymbol[symbol] ? `${fmtQty(bySymbol[symbol].quantity)} 股` : '未持有'}`,
        ).join('、')}
        （取自持股快照，建議股數依此計算）
      </Typography>
    </Box>
  );
}

interface SettingsPanelProps {
  settings: BrakeSettings;
  onChange: (next: BrakeSettings) => void;
}

function SettingsPanel({ settings, onChange }: SettingsPanelProps) {
  const [advancedOpen, setAdvancedOpen] = useState(false);
  const [kText, setKText] = useState(
    settings.minCashRatio != null ? String(settings.minCashRatio) : '',
  );

  const profile = PROFILES[settings.profile];
  const effectiveK = settings.minCashRatio ?? profile.k;

  const parsedK = kText.trim() === '' ? undefined : Number(kText);
  const kInvalid =
    parsedK !== undefined &&
    (!Number.isFinite(parsedK) || parsedK < MIN_CASH_RATIO_MIN || parsedK > MIN_CASH_RATIO_MAX);

  const handleK = (raw: string) => {
    setKText(raw);
    if (raw.trim() === '') {
      onChange({ ...settings, minCashRatio: undefined });
      return;
    }
    const n = Number(raw);
    if (Number.isFinite(n) && n >= MIN_CASH_RATIO_MIN && n <= MIN_CASH_RATIO_MAX) {
      onChange({ ...settings, minCashRatio: n });
    }
  };

  return (
    <Box sx={{ mb: 2 }}>
      <Typography variant="body2" sx={{ fontWeight: 600, mb: 1 }}>
        風險等級
      </Typography>
      <ToggleButtonGroup
        exclusive
        fullWidth
        size="small"
        color="primary"
        aria-label="風險等級"
        value={settings.profile}
        onChange={(_, next: ProfileKey | null) => {
          if (next) onChange({ ...settings, profile: next });
        }}
      >
        {PROFILE_KEYS.map((key) => (
          <ToggleButton key={key} value={key}>
            {PROFILES[key].label}
          </ToggleButton>
        ))}
      </ToggleButtonGroup>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
        進攻有效曝險 {fmtMultiple(profile.eOn)} 倍、防守 {fmtMultiple(settings.defenseE)}{' '}
        倍、最低現金 {Math.round(effectiveK * 100)}%
      </Typography>

      <Button
        size="small"
        onClick={() => setAdvancedOpen((open) => !open)}
        sx={{ mt: 1 }}
        aria-expanded={advancedOpen}
      >
        {advancedOpen ? '收合進階設定' : '進階設定'}
      </Button>
      <Collapse in={advancedOpen} unmountOnExit>
        <Box sx={{ pt: 1 }}>
          <Typography variant="body2" sx={{ fontWeight: 600, mb: 1 }}>
            防守時的有效曝險 E
          </Typography>
          <ToggleButtonGroup
            exclusive
            fullWidth
            size="small"
            color="primary"
            aria-label="防守時的有效曝險 E"
            value={settings.defenseE}
            onChange={(_, next: number | null) => {
              if (next != null) onChange({ ...settings, defenseE: next === 0.3 ? 0.3 : 0 });
            }}
          >
            {DEFENSE_E_OPTIONS.map((e) => (
              <ToggleButton key={e} value={e}>
                {e === 0 ? '0（全現金）' : '0.3（0050 30% / 現金 70%）'}
              </ToggleButton>
            ))}
          </ToggleButtonGroup>
          <TextField
            size="small"
            type="number"
            label="最低現金比例 k"
            value={kText}
            onChange={(e) => handleK(e.target.value)}
            error={kInvalid}
            fullWidth
            sx={{ mt: 2 }}
            placeholder={String(profile.k)}
            helperText={
              kInvalid
                ? `請輸入 ${MIN_CASH_RATIO_MIN} 到 ${MIN_CASH_RATIO_MAX} 之間的數值`
                : `${MIN_CASH_RATIO_MIN} ~ ${MIN_CASH_RATIO_MAX}，留空則依風險等級（目前 ${profile.k}）`
            }
            slotProps={{
              htmlInput: { min: MIN_CASH_RATIO_MIN, max: MIN_CASH_RATIO_MAX, step: 0.05 },
            }}
          />
        </Box>
      </Collapse>
    </Box>
  );
}

/** 風險等級說明（#431 §10 三點）與回測績效。績效一律同時顯示期間與最大回撤，且不單獨突顯最高
 * 報酬（PM 的 R3）；用隔日成交版本，與第一階段「收盤後才提醒」的時點一致（R4）。 */
function RiskExplanation() {
  return (
    <Accordion
      disableGutters
      elevation={0}
      sx={{
        border: '1px solid',
        borderColor: 'divider',
        borderRadius: 2,
        '&:before': { display: 'none' },
      }}
    >
      <AccordionSummary expandIcon={<ExpandMoreIcon />}>
        <Typography variant="body2" sx={{ fontWeight: 600 }}>
          風險等級說明
        </Typography>
      </AccordionSummary>
      <AccordionDetails>
        <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
          {RISK_POINTS.map((point) => (
            <Typography
              component="li"
              variant="body2"
              key={point}
              sx={{ mb: 0.5, overflowWrap: 'anywhere' }}
            >
              {point}
            </Typography>
          ))}
        </Box>

        <Typography variant="body2" sx={{ fontWeight: 600, mt: 2, mb: 1 }}>
          歷史回測（{BACKTEST_PERIOD}，隔日成交版本）
        </Typography>
        <Typography variant="body2" sx={{ mb: 1, fontWeight: 600, overflowWrap: 'anywhere' }}>
          {BACKTEST_SCOPE_NOTE}
        </Typography>
        <Box role="table" aria-label="歷史回測績效" sx={{ display: 'grid', gap: 0.5 }}>
          <Box
            role="row"
            sx={{
              display: 'grid',
              gridTemplateColumns: '1fr 1fr 1fr',
              gap: 1,
              color: 'text.secondary',
            }}
          >
            <Typography role="columnheader" variant="caption">
              等級
            </Typography>
            <Typography role="columnheader" variant="caption">
              年化報酬
            </Typography>
            <Typography role="columnheader" variant="caption">
              最大回撤
            </Typography>
          </Box>
          {PROFILE_KEYS.map((key) => (
            <Box
              role="row"
              key={key}
              sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: 1 }}
            >
              <Typography role="cell" variant="body2">
                {PROFILES[key].label}
              </Typography>
              <Typography role="cell" variant="body2">
                {BACKTEST_NEXT_DAY[key].annualPct.toFixed(1)}%
              </Typography>
              <Typography role="cell" variant="body2">
                {BACKTEST_NEXT_DAY[key].maxDrawdownPct}%
              </Typography>
            </Box>
          ))}
        </Box>
        <Typography
          variant="caption"
          color="text.secondary"
          sx={{ display: 'block', mt: 1, overflowWrap: 'anywhere' }}
        >
          {`回測期間 ${BACKTEST_PERIOD}。本站只在收盤後提醒，等同隔日成交，所以採用隔日成交版本的數字。${SIMULATED_NOTE}`}
        </Typography>
      </AccordionDetails>
    </Accordion>
  );
}

function DrawdownBrakeContent({ summary }: { summary: PortfolioSummaryDto }) {
  const [settings, setSettings] = useState<BrakeSettings>(loadBrakeSettings);
  const { data, isLoading, isError, error, refetch } = useGetDrawdownBrakeQuery();
  const signal = data?.data;
  const active = signal ? pickActiveSignal(signal) : null;
  // 重新整理要重抓報價的標的：使用者持有的核心標的（0050、00631L、00685L），加上範圍內價格取得失敗
  // 的股票（例如 NTSD）— 失敗會暫停整張卡片的建議，這是使用者唯一的補救手段。
  const refreshSymbols = priceRefreshSymbols(
    extractStrategyPositions(summary.positions),
    classifyPositions(summary.positions).priceFailed,
  );

  const handleChange = (next: BrakeSettings) => {
    setSettings(next);
    saveBrakeSettings(next);
  };

  return (
    <Box sx={{ minWidth: 0 }}>
      <Disclaimer />

      {isLoading && <Skeleton variant="rounded" height={96} sx={{ mb: 2 }} />}

      {isError && !signal && (
        <Alert
          severity="error"
          sx={{ mb: 2 }}
          action={
            <Button color="inherit" size="small" onClick={() => refetch()}>
              重試
            </Button>
          }
        >
          {getApiErrorMessage(error)}
        </Alert>
      )}

      {signal?.isInitializing && (
        <Alert severity="info" sx={{ mb: 2 }}>
          資料準備中，大盤訊號建立完成後會顯示在這裡，請稍後再回來看看。
        </Alert>
      )}

      {signal?.estimateCorrected && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          訊號已更正：先前的盤中預估與收盤後的正式訊號不同，已以正式訊號為準。
        </Alert>
      )}

      {signal?.isStale && !signal.isInitializing && (
        <Alert severity="warning" sx={{ mb: 2 }}>
          資料未更新（資料截止 {signal.asOfDate ?? '—'}），目前沿用最近一個交易日的狀態。
        </Alert>
      )}

      {signal && !signal.isInitializing && !active && (
        <Alert severity="info" sx={{ mb: 2 }}>
          目前沒有可用的大盤訊號。
        </Alert>
      )}

      {/* 資料時間＋重新整理：有訊號資料的所有狀態都顯示（準備中、未更新、預估、正常）。 */}
      {signal && <DrawdownBrakeFreshness signal={signal} refreshSymbols={refreshSymbols} />}

      {signal && active && (
        <>
          <SignalSummary signal={signal} active={active} />
          <AdvicePanel summary={summary} settings={settings} active={active} />
        </>
      )}

      <HoldingsInfo summary={summary} />
      <SettingsPanel settings={settings} onChange={handleChange} />
      <RiskExplanation />
    </Box>
  );
}

interface DrawdownBrakeCardProps {
  summary: PortfolioSummaryDto;
}

/** 「回落煞車」：只在 `drawdownBrakeEnabled` 為 true 時才掛載內容 — 內容才會發請求，所以功能關閉
 * 時（Q1 未決前一般使用者）既看不到，也不會產生任何 API 呼叫。不呼叫 AI、不消耗 UsageLimit。 */
export function DrawdownBrakeCard({ summary }: DrawdownBrakeCardProps) {
  const { data: userRes } = useGetUserInfoQuery();
  if (!userRes?.data.drawdownBrakeEnabled) return null;
  return (
    <ChartSectionCard title="回落煞車">
      <DrawdownBrakeContent summary={summary} />
    </ChartSectionCard>
  );
}
