import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import Accordion from '@mui/material/Accordion';
import AccordionDetails from '@mui/material/AccordionDetails';
import AccordionSummary from '@mui/material/AccordionSummary';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Collapse from '@mui/material/Collapse';
import InputAdornment from '@mui/material/InputAdornment';
import Skeleton from '@mui/material/Skeleton';
import TextField from '@mui/material/TextField';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import { ChartSectionCard } from './ChartSectionCard';
import {
  advise,
  BrakeSettings,
  BrakeState,
  composition,
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
  StrategySymbol,
} from './drawdownBrake';
import {
  adviceMessage,
  BACKTEST_NEXT_DAY,
  BACKTEST_PERIOD,
  describeComposition,
  describeOrder,
  DISCLAIMER,
  fmtMultiple,
  RISK_POINTS,
  SIMULATED_NOTE,
  STATE_LABEL,
} from './drawdownBrakeCopy';
import {
  extractStrategyPositions,
  findSplitGuardHit,
  isSnapshotStale,
  splitGuardMessage,
  STALE_SNAPSHOT_MESSAGE,
  sumTwdCash,
} from './drawdownBrakeHoldings';
import { usePrivacy } from './privacy';

import { useGetUserInfoQuery } from '@/apis/accountApis';
import { useGetDrawdownBrakeQuery, useLookupStockQuery } from '@/apis/portfolioApis';
import { DrawdownBrakeDto, PortfolioSummaryDto } from '@/apis/portfolioApis/types';
import { getApiErrorMessage } from '@/apis/utils/errorMessage';
import { todayISO } from '@/utils/date';
import { fmtNum, fmtQty } from '@/utils/format';

const STRATEGY_SYMBOLS: StrategySymbol[] = ['0050', '00631L'];

function isValidPrice(price: number | null | undefined): price is number {
  return typeof price === 'number' && Number.isFinite(price) && price > 0;
}

/** 持有且快照有價格 → 用快照價（已含交易時段的即時價）；否則（沒持有，或價格抓取失敗）查價。
 * 沒持有 0050 / 00631L 的使用者（PM 假設 A4）也要有價格才能換算建議股數。 */
function useStrategyPrice(symbol: StrategySymbol, summaryPrice: number | null) {
  const needLookup = summaryPrice == null;
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

function AdvicePanel({ summary, settings, active }: AdvicePanelProps) {
  const { value } = usePrivacy();
  const params = resolveParams(settings);
  const profileLabel = PROFILES[settings.profile].label;

  const held = extractStrategyPositions(summary.positions);
  const heldBySymbol = Object.fromEntries(held.map((h) => [h.symbol, h])) as Partial<
    Record<StrategySymbol, (typeof held)[number]>
  >;
  const price0050 = useStrategyPrice('0050', heldBySymbol['0050']?.price ?? null);
  const price00631L = useStrategyPrice('00631L', heldBySymbol['00631L']?.price ?? null);
  const priceInfo = { '0050': price0050, '00631L': price00631L };

  const target = composition(active.state === 'OFFENSE' ? params.eOn : params.eOff, params.k);
  const guardHit = findSplitGuardHit(held, summary.effectiveDate);
  const snapshotStale = isSnapshotStale(summary.effectiveDate, todayISO());

  const marketValue = (symbol: StrategySymbol) => {
    const price = priceInfo[symbol].price;
    return (heldBySymbol[symbol]?.quantity ?? 0) * (price ?? 0);
  };
  const holdings = {
    '0050': marketValue('0050'),
    '00631L': marketValue('00631L'),
    cash: settings.strategyCashTWD,
  };
  const totalValue = holdings['0050'] + holdings['00631L'] + holdings.cash;

  const priceLoading = STRATEGY_SYMBOLS.some((s) => priceInfo[s].loading);
  const missingPrice = STRATEGY_SYMBOLS.find(
    (s) => priceInfo[s].price == null && !priceInfo[s].loading,
  );

  const prices = {
    '0050': priceInfo['0050'].price ?? 0,
    '00631L': priceInfo['00631L'].price ?? 0,
  };
  const advice =
    !guardHit && !missingPrice && !priceLoading && totalValue > 0
      ? advise(active.prevState, active.state, params, holdings, prices)
      : null;

  return (
    <Box sx={{ mb: 2 }}>
      {snapshotStale && (
        <Alert severity="warning" sx={{ mb: 1.5 }}>
          {STALE_SNAPSHOT_MESSAGE}
        </Alert>
      )}
      {target.truncated && (
        <Alert severity="warning" sx={{ mb: 1.5 }}>
          現金比例過高，無法達到此曝險
        </Alert>
      )}

      {guardHit && (
        <>
          <Typography variant="body1" sx={{ mb: 1.5, overflowWrap: 'anywhere' }}>
            {`目前狀態：${STATE_LABEL[active.state]}。依『${profileLabel}』等級，目標配置：${describeComposition(target.weights)}（有效曝險 ${fmtMultiple(target.effectiveE)} 倍）。`}
          </Typography>
          <Alert severity="error" sx={{ mb: 1.5 }}>
            {splitGuardMessage(guardHit)}
          </Alert>
        </>
      )}

      {!guardHit && totalValue <= 0 && (
        <Alert severity="info" sx={{ mb: 1.5 }}>
          請先在下方輸入策略現金（NT$），或在持股中新增 0050／00631L，才能算出目標配置與建議股數。
        </Alert>
      )}

      {!guardHit && totalValue > 0 && priceLoading && <Skeleton variant="rounded" height={64} />}

      {!guardHit && totalValue > 0 && !priceLoading && missingPrice && (
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
          {advice.orders.length > 0 && (
            <Box component="ul" aria-label="建議買賣股數" sx={{ m: 0, mt: 1.5, pl: 2.5 }}>
              {advice.orders.map((order) => (
                <Typography
                  component="li"
                  variant="body2"
                  key={order.symbol}
                  sx={{ fontWeight: 600 }}
                >
                  {describeOrder(order)}
                </Typography>
              ))}
            </Box>
          )}
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1.5 }}>
            {`目標配置：${describeComposition(advice.weights)}｜有效曝險 ${fmtMultiple(advice.eActual)} 倍（目標 ${fmtMultiple(advice.eTarget)} 倍）｜策略總資產 NT$${value(fmtNum(advice.totalValue))}`}
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
        {STRATEGY_SYMBOLS.map(
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
  twdCash: number;
  onChange: (next: BrakeSettings) => void;
}

function SettingsPanel({ settings, twdCash, onChange }: SettingsPanelProps) {
  const { value } = usePrivacy();
  const [advancedOpen, setAdvancedOpen] = useState(false);
  const [cashText, setCashText] = useState(
    settings.strategyCashTWD > 0 ? String(settings.strategyCashTWD) : '',
  );
  const [kText, setKText] = useState(
    settings.minCashRatio != null ? String(settings.minCashRatio) : '',
  );

  const profile = PROFILES[settings.profile];
  const effectiveK = settings.minCashRatio ?? profile.k;

  const parsedCash = cashText.trim() === '' ? 0 : Number(cashText);
  const cashInvalid = !Number.isFinite(parsedCash) || parsedCash < 0;
  const parsedK = kText.trim() === '' ? undefined : Number(kText);
  const kInvalid =
    parsedK !== undefined &&
    (!Number.isFinite(parsedK) || parsedK < MIN_CASH_RATIO_MIN || parsedK > MIN_CASH_RATIO_MAX);

  const handleCash = (raw: string) => {
    setCashText(raw);
    const n = raw.trim() === '' ? 0 : Number(raw);
    if (Number.isFinite(n) && n >= 0) onChange({ ...settings, strategyCashTWD: n });
  };
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

      <TextField
        size="small"
        type="number"
        label="策略現金"
        value={cashText}
        onChange={(e) => handleCash(e.target.value)}
        error={cashInvalid}
        fullWidth
        sx={{ mt: 2 }}
        helperText={
          cashInvalid
            ? '請輸入 0 以上的金額'
            : `指定給這個策略使用的現金。你的 TWD 現金部位合計 NT$${value(fmtNum(twdCash))}（供參考，不會自動採用）`
        }
        slotProps={{
          input: { startAdornment: <InputAdornment position="start">NT$</InputAdornment> },
          htmlInput: { min: 0, step: 1000, inputMode: 'numeric' },
        }}
      />

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

      {signal && active && (
        <>
          <SignalSummary signal={signal} active={active} />
          <AdvicePanel summary={summary} settings={settings} active={active} />
        </>
      )}

      <HoldingsInfo summary={summary} />
      <SettingsPanel
        settings={settings}
        twdCash={sumTwdCash(summary.positions)}
        onChange={handleChange}
      />
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
