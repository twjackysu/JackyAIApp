import CloseIcon from '@mui/icons-material/Close';
import FullscreenIcon from '@mui/icons-material/Fullscreen';
import FullscreenExitIcon from '@mui/icons-material/FullscreenExit';
import NavigateBeforeIcon from '@mui/icons-material/NavigateBefore';
import NavigateNextIcon from '@mui/icons-material/NavigateNext';
import PauseIcon from '@mui/icons-material/Pause';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import { Box, Checkbox, FormControlLabel, IconButton, Slider, Typography } from '@mui/material';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';

import { ObjectUrlWindow } from './objectUrlWindow';
import { shuffledOrder } from './photoFiles';

import type { PhotoFile } from './photoFiles';
import type { SlideshowSettings } from './settings';

import { ActionTooltip } from '@/components/hints';

interface Props {
  photos: PhotoFile[];
  settings: SlideshowSettings;
  onSettingsChange: (patch: Partial<SlideshowSettings>) => void;
  onExit: () => void;
}

/**
 * 全螢幕覆蓋的播放器。用 `position: fixed` 蓋掉 App 外層的 AppBar / padding / Footer，
 * 不必為了一個頁面去改共用版面。
 */
function SlideshowPlayer({ photos, settings, onSettingsChange, onExit }: Props) {
  const [index, setIndex] = useState(0);
  const [playing, setPlaying] = useState(true);
  const [idle, setIdle] = useState(false);
  const [isFullscreen, setIsFullscreen] = useState(false);

  // 交叉淡入用的兩層：prev 墊在底下，cur 疊上去淡入。
  //
  // 換圖的時機由 Image.decode() 決定，不靠 <img> 的 onLoad：blob URL 視窗會讓最近看過的
  // 照片保持同一個 URL 字串，往回翻時同一層會被指派到它原本就有的 src —— src 沒變就不會
  // 有 load 事件，畫面便卡在上一張，而計數器已經前進了。decode() 不管 URL 之前出現過沒有
  // 都會 resolve。
  const [layers, setLayers] = useState<{ prev: string | null; cur: string | null }>({
    prev: null,
    cur: null,
  });
  // 整個資料夾沒有一張瀏覽器解得開（最常見是 Chrome 開 iPhone 的 .heic）。
  const [unplayable, setUnplayable] = useState(false);
  // 遇到解不開的檔案時往哪邊跳：使用者按 ← 撞到壞檔，應該繼續往前而不是被彈回來。
  const direction = useRef<1 | -1>(1);
  const consecutiveFailures = useRef(0);

  const rootRef = useRef<HTMLDivElement>(null);
  const idleTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const advanceTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  // 播放順序：order[index] 是 photos 裡的第幾張。沒開隨機時就是 0, 1, 2…，也就是檔名順序。
  // photos 本身永遠保持檔名順序，關掉隨機時才回得去。
  const [order, setOrder] = useState<number[]>(() =>
    settings.shuffle
      ? shuffledOrder(photos.length, Math.floor(Math.random() * photos.length))
      : photos.map((_, i) => i),
  );
  const files = useMemo(() => order.map((i) => photos[i].file), [order, photos]);
  // useState 的 lazy init 而不是 useRef：只要建立一次，而且 render 期間不碰 ref。
  const [urlWindow] = useState(() => new ObjectUrlWindow(1));

  const total = photos.length;
  const go = useCallback(
    (next: number, dir: 1 | -1) => {
      direction.current = dir;
      setIndex(((next % total) + total) % total);
    },
    [total],
  );

  // 一次只保留目前這張前後各一張的 blob URL，其餘釋放 —— 照片資料夾動輒上千張。
  useEffect(() => {
    urlWindow.sync(files, index);

    const url = urlWindow.get(files[index]);
    if (!url) return;

    // 快速連按時只有最後一次請求算數；前面還在解碼的結果晚到也不能蓋掉現在這張。
    let cancelled = false;
    const probe = new Image();
    probe.src = url;
    probe.decode().then(
      () => {
        if (cancelled) return;
        consecutiveFailures.current = 0;
        setLayers((l) => (l.cur === url ? l : { prev: l.cur, cur: url }));
      },
      () => {
        if (cancelled) return;
        // 解不開就照原本的方向跳過，不論是否在播放 —— 暫停時停在壞檔上，畫面會是上一張
        // 但計數器指著壞檔，兩邊對不起來。
        consecutiveFailures.current += 1;
        if (consecutiveFailures.current >= files.length) {
          // 一張都解不開：停下來說明原因，而不是無限輪轉。
          setUnplayable(true);
          setPlaying(false);
          return;
        }
        const n = files.length;
        setIndex((((index + direction.current) % n) + n) % n);
      },
    );
    return () => {
      cancelled = true;
    };
  }, [files, index, urlWindow]);

  // 開關隨機播放。畫面上這張不換，只改「接下來播哪張」—— 使用者勾的是之後的順序，
  // 不是要現在跳到別張。
  const toggleShuffle = useCallback(
    (on: boolean) => {
      const currentPhoto = order[index];
      setOrder(on ? shuffledOrder(photos.length, currentPhoto) : photos.map((_, i) => i));
      // 隨機：這張排在新順序的第一個。檔名順序：回到這張原本的位置。
      setIndex(on ? 0 : currentPhoto);
      onSettingsChange({ shuffle: on });
    },
    [order, index, photos, onSettingsChange],
  );

  // 換資料夾或離開頁面時把所有 URL 釋放掉，否則舊資料夾的檔案會一直被瀏覽器釘著。
  useEffect(() => () => urlWindow.dispose(), [urlWindow]);

  // ── 自動播放 ──────────────────────────────────────────────────────────
  useEffect(() => {
    clearTimeout(advanceTimer.current);
    if (!playing || total === 0) return;

    advanceTimer.current = setTimeout(() => {
      if (!settings.loop && index === total - 1) {
        setPlaying(false);
        return;
      }
      go(index + 1, 1);
    }, settings.interval * 1000);

    return () => clearTimeout(advanceTimer.current);
  }, [playing, index, total, settings.interval, settings.loop, go]);

  // ── 滑鼠靜止就把控制列淡出 ────────────────────────────────────────────
  const wake = useCallback(() => {
    setIdle(false);
    clearTimeout(idleTimer.current);
    idleTimer.current = setTimeout(() => setIdle(true), 2500);
  }, []);

  useEffect(() => {
    wake();
    const events: (keyof WindowEventMap)[] = ['mousemove', 'keydown', 'click'];
    events.forEach((e) => window.addEventListener(e, wake));
    return () => {
      clearTimeout(idleTimer.current);
      events.forEach((e) => window.removeEventListener(e, wake));
    };
  }, [wake]);

  // ── 全螢幕 ────────────────────────────────────────────────────────────
  const toggleFullscreen = useCallback(() => {
    if (document.fullscreenElement) void document.exitFullscreen();
    else void rootRef.current?.requestFullscreen();
  }, []);

  useEffect(() => {
    const onChange = () => setIsFullscreen(Boolean(document.fullscreenElement));
    document.addEventListener('fullscreenchange', onChange);
    return () => document.removeEventListener('fullscreenchange', onChange);
  }, []);

  // ── 鍵盤 ──────────────────────────────────────────────────────────────
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.target instanceof HTMLElement && e.target.matches('input, select, textarea')) return;
      switch (e.key) {
        case ' ':
          e.preventDefault();
          setPlaying((p) => !p);
          break;
        case 'ArrowRight':
        case 'PageDown':
          go(index + 1, 1);
          break;
        case 'ArrowLeft':
        case 'PageUp':
          go(index - 1, -1);
          break;
        case 'Home':
          go(0, 1);
          break;
        case 'End':
          go(total - 1, -1);
          break;
        case 'f':
        case 'F':
          toggleFullscreen();
          break;
        case 'Escape':
          // 全螢幕時 Esc 被瀏覽器收走去離開全螢幕，這裡只處理非全螢幕的情況。
          if (!document.fullscreenElement) onExit();
          break;
        case '+':
        case '=':
          onSettingsChange({ interval: Math.min(30, settings.interval + 1) });
          break;
        case '-':
          onSettingsChange({ interval: Math.max(1, settings.interval - 1) });
          break;
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [index, total, go, toggleFullscreen, onExit, onSettingsChange, settings.interval]);

  const current = photos[order[index]];

  const chromeSx = {
    position: 'absolute' as const,
    left: 0,
    right: 0,
    zIndex: 5,
    px: 2,
    py: 1.25,
    display: 'flex',
    gap: 1.5,
    alignItems: 'center',
    flexWrap: 'wrap' as const,
    opacity: idle ? 0 : 1,
    pointerEvents: idle ? ('none' as const) : ('auto' as const),
    transition: 'opacity .25s',
  };

  return (
    <Box
      ref={rootRef}
      sx={{
        position: 'fixed',
        inset: 0,
        zIndex: 1300,
        bgcolor: '#000',
        cursor: idle ? 'none' : 'default',
      }}
    >
      <Box
        onClick={(e) => {
          // 點畫面本身播放/暫停；點控制列上的按鈕不算。
          if (e.target === e.currentTarget) setPlaying((p) => !p);
        }}
        sx={{ position: 'absolute', inset: 0 }}
      >
        {/* key 用 URL：換圖時舊的 cur 元素原封不動搬到 prev 的位置，不會重新載入，也就
            不會碰到已經被 revoke 的 URL。只有 cur 帶淡入動畫。 */}
        {[layers.prev, layers.cur].map(
          (url, i) =>
            url && (
              <Box
                key={url}
                component="img"
                src={url}
                alt={i === 1 ? (current?.name ?? '') : ''}
                sx={{
                  position: 'absolute',
                  inset: 0,
                  width: '100%',
                  height: '100%',
                  objectFit: settings.fit,
                  // 黑底不能省。「完整顯示」時照片四周的留白是 img 自己的背景，預設透明 ——
                  // 直的照片疊在橫的上面，底下那張會從兩側露出來，看起來像兩張疊在一起。
                  // 有了黑底，新照片淡入時連同留白一起蓋住舊照片。
                  bgcolor: '#000',
                  pointerEvents: 'none',
                  animation: i === 1 ? 'slideshow-fade .4s ease' : 'none',
                  '@keyframes slideshow-fade': { from: { opacity: 0 }, to: { opacity: 1 } },
                }}
              />
            ),
        )}
        {unplayable && (
          <Box
            sx={{
              position: 'absolute',
              inset: 0,
              display: 'grid',
              placeItems: 'center',
              p: 3,
              pointerEvents: 'none',
            }}
          >
            <Typography sx={{ color: 'grey.300', textAlign: 'center', maxWidth: 480 }}>
              這個資料夾裡的照片瀏覽器一張都顯示不了。
              <br />
              最常見的原因是 iPhone 的 .heic —— 只有 Safari 能直接顯示，Chrome / Edge 不行。
            </Typography>
          </Box>
        )}
      </Box>

      {/* 上列：離開 + 檔名 + 張數 + 全螢幕 */}
      <Box
        sx={{
          ...chromeSx,
          top: 0,
          background: 'linear-gradient(to bottom, rgba(0,0,0,.75), rgba(0,0,0,0))',
        }}
      >
        <ActionTooltip title="回到選擇資料夾 (Esc)">
          <IconButton size="small" onClick={onExit} sx={{ color: 'common.white' }}>
            <CloseIcon fontSize="small" />
          </IconButton>
        </ActionTooltip>
        <Typography
          variant="caption"
          sx={{
            color: 'grey.400',
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            whiteSpace: 'nowrap',
            maxWidth: '55vw',
          }}
        >
          {current?.path}
        </Typography>
        <Box sx={{ flex: 1 }} />
        <Typography variant="caption" sx={{ color: 'grey.400' }}>
          {index + 1} / {total}
        </Typography>
        <ActionTooltip title={isFullscreen ? '離開全螢幕 (F)' : '全螢幕 (F)'}>
          <IconButton size="small" onClick={toggleFullscreen} sx={{ color: 'common.white' }}>
            {isFullscreen ? (
              <FullscreenExitIcon fontSize="small" />
            ) : (
              <FullscreenIcon fontSize="small" />
            )}
          </IconButton>
        </ActionTooltip>
      </Box>

      {/* 下列：播放控制 */}
      <Box
        sx={{
          ...chromeSx,
          bottom: 0,
          background: 'linear-gradient(to top, rgba(0,0,0,.75), rgba(0,0,0,0))',
        }}
      >
        <ActionTooltip title="上一張 (←)">
          <IconButton size="small" onClick={() => go(index - 1, -1)} sx={{ color: 'common.white' }}>
            <NavigateBeforeIcon />
          </IconButton>
        </ActionTooltip>
        <ActionTooltip title={playing ? '暫停 (Space)' : '播放 (Space)'}>
          <IconButton
            size="small"
            onClick={() => setPlaying((p) => !p)}
            sx={{ color: 'common.white' }}
          >
            {playing ? <PauseIcon /> : <PlayArrowIcon />}
          </IconButton>
        </ActionTooltip>
        <ActionTooltip title="下一張 (→)">
          <IconButton size="small" onClick={() => go(index + 1, 1)} sx={{ color: 'common.white' }}>
            <NavigateNextIcon />
          </IconButton>
        </ActionTooltip>

        <Typography variant="caption" sx={{ color: 'grey.400', ml: 1, minWidth: 28 }}>
          {settings.interval}s
        </Typography>
        <Slider
          size="small"
          min={1}
          max={30}
          value={settings.interval}
          onChange={(_, v) => onSettingsChange({ interval: v as number })}
          aria-label="每張秒數"
          sx={{ width: 120, color: 'primary.light' }}
        />

        <FormControlLabel
          control={
            <Checkbox
              size="small"
              checked={settings.loop}
              onChange={(e) => onSettingsChange({ loop: e.target.checked })}
              sx={{ color: 'grey.500' }}
            />
          }
          label={<Typography variant="caption">循環</Typography>}
          sx={{ color: 'grey.400', mr: 0 }}
        />
        <FormControlLabel
          control={
            <Checkbox
              size="small"
              checked={settings.shuffle}
              onChange={(e) => toggleShuffle(e.target.checked)}
              sx={{ color: 'grey.500' }}
            />
          }
          label={<Typography variant="caption">隨機播放</Typography>}
          sx={{ color: 'grey.400', mr: 0 }}
        />
        <FormControlLabel
          control={
            <Checkbox
              size="small"
              checked={settings.fit === 'cover'}
              onChange={(e) => onSettingsChange({ fit: e.target.checked ? 'cover' : 'contain' })}
              sx={{ color: 'grey.500' }}
            />
          }
          label={<Typography variant="caption">填滿裁切</Typography>}
          sx={{ color: 'grey.400', mr: 0 }}
        />
      </Box>

      {/* 進度條。key 讓它每張重新掛載，animation 才會從 0 重跑。 */}
      {playing && (
        <Box
          key={`${index}-${settings.interval}`}
          sx={{
            position: 'absolute',
            left: 0,
            bottom: 0,
            height: 3,
            zIndex: 6,
            bgcolor: 'primary.main',
            animation: `slideshow-progress ${settings.interval}s linear forwards`,
            '@keyframes slideshow-progress': { from: { width: '0%' }, to: { width: '100%' } },
          }}
        />
      )}
    </Box>
  );
}

export default SlideshowPlayer;
