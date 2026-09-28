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

  // 兩層 img 交叉淡入：新圖先載進背面那層，載好才翻面，中間不會閃一下黑畫面。
  const [slots, setSlots] = useState<[string, string]>(['', '']);
  const [activeSlot, setActiveSlot] = useState(0);
  const activeSlotRef = useRef(0);

  const rootRef = useRef<HTMLDivElement>(null);
  const idleTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const advanceTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  const files = useMemo(() => photos.map((p) => p.file), [photos]);
  // useState 的 lazy init 而不是 useRef：只要建立一次，而且 render 期間不碰 ref。
  const [urlWindow] = useState(() => new ObjectUrlWindow(1));

  const total = photos.length;
  const go = useCallback((next: number) => setIndex(((next % total) + total) % total), [total]);

  // 一次只保留目前這張前後各一張的 blob URL，其餘釋放 —— 照片資料夾動輒上千張。
  useEffect(() => {
    urlWindow.sync(files, index);

    const url = urlWindow.get(index);
    if (!url) return;
    const target = (activeSlotRef.current + 1) % 2;
    setSlots((prev) => {
      const next: [string, string] = [prev[0], prev[1]];
      next[target] = url;
      return next;
    });
  }, [files, index, urlWindow]);

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
      go(index + 1);
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
          go(index + 1);
          break;
        case 'ArrowLeft':
        case 'PageUp':
          go(index - 1);
          break;
        case 'Home':
          go(0);
          break;
        case 'End':
          go(total - 1);
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

  const current = photos[index];

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
        {[0, 1].map((slot) => (
          <Box
            key={slot}
            component="img"
            src={slots[slot] || undefined}
            alt={slot === activeSlot ? (current?.name ?? '') : ''}
            onLoad={() => {
              activeSlotRef.current = slot;
              setActiveSlot(slot);
            }}
            // 壞檔或瀏覽器解不開的格式（最常見是 .heic）直接跳過，不要停在黑畫面上。
            onError={() => {
              if (playing) go(index + 1);
            }}
            sx={{
              position: 'absolute',
              inset: 0,
              width: '100%',
              height: '100%',
              objectFit: settings.fit,
              opacity: slot === activeSlot ? 1 : 0,
              transition: 'opacity .4s ease',
              pointerEvents: 'none',
            }}
          />
        ))}
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
          <IconButton size="small" onClick={() => go(index - 1)} sx={{ color: 'common.white' }}>
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
          <IconButton size="small" onClick={() => go(index + 1)} sx={{ color: 'common.white' }}>
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
