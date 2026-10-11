import RefreshIcon from '@mui/icons-material/Refresh';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Typography from '@mui/material/Typography';
import { useEffect, useRef, useState } from 'react';

import {
  cooldownHint,
  lastCheckedLabel,
  PRICE_REFRESH_FAILED,
  REFRESH_BUTTON_BUSY_LABEL,
  REFRESH_BUTTON_LABEL,
  refreshFailedMessage,
  refreshResultMessage,
  signalDateLabel,
  throttledMessage,
} from './drawdownBrakeCopy';
import {
  formatLastChecked,
  PRICE_REFRESH_COOLDOWN_SECONDS,
  sanitizeRetryAfter,
  secondsLeft,
} from './drawdownBrakeRefresh';
import { loadFeeDiscountRate } from './feeDiscountRate';

import {
  useRefreshDrawdownBrakeMutation,
  useRefreshPositionPriceMutation,
} from '@/apis/portfolioApis';
import { DrawdownBrakeDto, DrawdownBrakeRefreshResult } from '@/apis/portfolioApis/types';
import { getCaughtErrorMessage } from '@/apis/utils/errorMessage';

/** 目前已知的 `result`。後端日後新增結果種類時，不認得的不顯示成功、也不亂猜文案。 */
const KNOWN_RESULTS: readonly string[] = [
  'Updated',
  'NoNewData',
  'SourceUnavailable',
  'Throttled',
] satisfies DrawdownBrakeRefreshResult[];

/** 最近一次按下按鈕、訊號請求的結果。 */
type RefreshOutcome =
  | { kind: 'idle' }
  | { kind: 'result'; result: DrawdownBrakeRefreshResult; asOfDate: string | null }
  | { kind: 'error'; message: string };

interface DrawdownBrakeRefreshProps {
  signal: DrawdownBrakeDto;
  /** 要重抓報價的代號：持有的核心標的（0050、00631L、00685L）與範圍內價格取得失敗的股票（見
   * `priceRefreshSymbols`）。沒持有的不呼叫 refresh-price。 */
  refreshSymbols: string[];
}

/**
 * 資料時間＋「重新整理資料」。
 *
 * 一次按下會同時：(1) 請全站重新向證交所取訊號（成功後 RTK 快取改用回應的 `signal`），(2) 對持有的
 * 核心標的（0050、00631L、00685L）與價格取得失敗的範圍內股票（例如 NTSD）重抓報價（既有
 * `refresh-price`，完成後 Summary 快取自動重抓）。不輪詢，只在按下時請求。
 *
 * 按下後按鈕停用 30 秒（客戶端冷卻，保護報價來源）；後端回 `Throttled` 時再以它給的秒數倒數。
 * 停用的原因一律寫在畫面上，不只靠 tooltip。
 */
export function DrawdownBrakeFreshness({ signal, refreshSymbols }: DrawdownBrakeRefreshProps) {
  const [refreshSignal] = useRefreshDrawdownBrakeMutation();
  const [refreshPrice] = useRefreshPositionPriceMutation();

  const [busy, setBusy] = useState(false);
  const [outcome, setOutcome] = useState<RefreshOutcome>({ kind: 'idle' });
  // 同時重抓持股報價的請求有失敗（與訊號結果無關，單獨提示）。
  const [priceFailed, setPriceFailed] = useState(false);
  // 兩個倒數各自記「結束的時間點」（epoch ms；0 ＝ 沒有）：客戶端冷卻（按下後 30 秒），以及後端
  // `Throttled` 給的秒數（全站冷卻）。用時間戳推算，分頁在背景被節流也不會失準。
  const [cooldownUntil, setCooldownUntil] = useState(0);
  const [throttleUntil, setThrottleUntil] = useState(0);
  const [now, setNow] = useState(() => Date.now());
  // 狀態更新有一個 render 的延遲；用 ref 擋掉同一瞬間的重複點擊。
  const inFlight = useRef(false);

  const lockUntil = Math.max(cooldownUntil, throttleUntil);
  useEffect(() => {
    if (lockUntil <= Date.now()) return;
    setNow(Date.now());
    const timer = setInterval(() => {
      const t = Date.now();
      setNow(t);
      if (t >= lockUntil) clearInterval(timer);
    }, 1000);
    return () => clearInterval(timer);
  }, [lockUntil]);

  const remaining = secondsLeft(lockUntil, now);
  const throttleRemaining = secondsLeft(throttleUntil, now);
  const locked = remaining > 0;
  const disabled = busy || locked;

  const handleRefresh = async () => {
    if (inFlight.current || disabled) return;
    inFlight.current = true;
    setBusy(true);
    setOutcome({ kind: 'idle' });
    setPriceFailed(false);
    const pressedAt = Date.now();
    setNow(pressedAt);
    setCooldownUntil(pressedAt + PRICE_REFRESH_COOLDOWN_SECONDS * 1000);

    try {
      const feeDiscountRate = loadFeeDiscountRate();
      const [signalRes, ...priceRes] = await Promise.allSettled([
        refreshSignal().unwrap(),
        ...refreshSymbols.map((symbol) => refreshPrice({ symbol, feeDiscountRate }).unwrap()),
      ]);
      setPriceFailed(priceRes.some((r) => r.status === 'rejected'));

      if (signalRes.status === 'rejected') {
        // 訊號請求本身失敗：快取沒動，舊資料照舊顯示。
        setOutcome({ kind: 'error', message: getCaughtErrorMessage(signalRes.reason) });
        return;
      }

      const { result, retryAfterSeconds, signal: latest } = signalRes.value.data;
      if (!KNOWN_RESULTS.includes(result)) return;
      if (result === 'Throttled') {
        const t = Date.now();
        setNow(t);
        setThrottleUntil(t + sanitizeRetryAfter(retryAfterSeconds) * 1000);
      }
      setOutcome({ kind: 'result', result, asOfDate: latest.asOfDate });
    } finally {
      inFlight.current = false;
      setBusy(false);
    }
  };

  const lastChecked = formatLastChecked(signal.lastCheckedAt);

  // 全站冷卻的倒數歸零後，「請 N 秒後再試」就不再成立，這則訊息一併收起。
  const throttledActive = outcome.kind === 'result' && outcome.result === 'Throttled';
  const showThrottled = throttledActive && throttleRemaining > 0;
  const showResult = outcome.kind === 'result' && (!throttledActive || showThrottled);

  return (
    <Box sx={{ mb: 2 }}>
      <Box
        sx={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          flexWrap: 'wrap',
          gap: 1,
        }}
      >
        {/* 兩段各自成一個單位；放不下時整段換到下一行，不從字中間切開。 */}
        <Box sx={{ display: 'flex', flexWrap: 'wrap', columnGap: 2, rowGap: 0.25, minWidth: 0 }}>
          <Typography variant="body2" sx={{ fontWeight: 600, overflowWrap: 'anywhere' }}>
            {signalDateLabel(signal.asOfDate)}
          </Typography>
          {lastChecked != null && (
            <Typography variant="body2" sx={{ fontWeight: 600, overflowWrap: 'anywhere' }}>
              {lastCheckedLabel(lastChecked)}
            </Typography>
          )}
        </Box>
        <Button
          variant="outlined"
          size="small"
          disabled={disabled}
          aria-busy={busy}
          onClick={handleRefresh}
          startIcon={busy ? <CircularProgress size={16} color="inherit" /> : <RefreshIcon />}
        >
          {busy ? REFRESH_BUTTON_BUSY_LABEL : REFRESH_BUTTON_LABEL}
        </Button>
      </Box>

      {/* 常駐的 live region：訊息出現時螢幕閱讀器才會念。 */}
      <Box aria-live="polite" sx={{ minWidth: 0 }}>
        {showResult && outcome.kind === 'result' && (
          <Alert
            severity={resultSeverity(outcome.result)}
            // 「資料未更新」是警告，維持 alert 角色；其餘為一般狀態通知。
            role={outcome.result === 'SourceUnavailable' ? 'alert' : 'status'}
            sx={{ mt: 1 }}
          >
            {outcome.result === 'Throttled'
              ? throttledMessage(throttleRemaining)
              : refreshResultMessage(outcome.result, outcome.asOfDate)}
          </Alert>
        )}

        {outcome.kind === 'error' && (
          <Alert severity="error" sx={{ mt: 1 }}>
            {refreshFailedMessage(outcome.message)}
          </Alert>
        )}

        {priceFailed && (
          <Typography
            variant="caption"
            color="text.secondary"
            sx={{ display: 'block', mt: 0.5, overflowWrap: 'anywhere' }}
          >
            {PRICE_REFRESH_FAILED}
          </Typography>
        )}

        {/* 按鈕為什麼停用：全站冷卻的原因已寫在上面的訊息裡，其餘（客戶端 30 秒冷卻）在這裡說明。 */}
        {locked && !busy && !showThrottled && (
          <Typography
            variant="caption"
            color="text.secondary"
            sx={{ display: 'block', mt: 0.5, overflowWrap: 'anywhere' }}
          >
            {cooldownHint(remaining)}
          </Typography>
        )}
      </Box>
    </Box>
  );
}

function resultSeverity(result: DrawdownBrakeRefreshResult): 'success' | 'info' | 'warning' {
  switch (result) {
    case 'Updated':
      return 'success';
    case 'SourceUnavailable':
      return 'warning';
    default:
      return 'info';
  }
}
