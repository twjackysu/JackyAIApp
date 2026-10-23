import { Box } from '@mui/material';
import { useEffect, useRef } from 'react';

import { ControlBar } from './ControlBar';
import { PhotoLayers } from './PhotoLayers';
import { clampInterval, playerKeyAction } from './playerKeys';
import { TopBar } from './TopBar';
import { useFullscreen } from './useFullscreen';
import { useIdle } from './useIdle';
import { useSlideshow } from './useSlideshow';

import type { PhotoFile } from '../photoFiles';
import type { SlideshowSettings } from '../settings';

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
  const show = useSlideshow(photos, settings, onSettingsChange);
  // 滑鼠靜止就把控制列淡出。
  const idle = useIdle(2500);
  const rootRef = useRef<HTMLDivElement>(null);
  const fullscreen = useFullscreen(rootRef);

  // ── 鍵盤 ──────────────────────────────────────────────────────────────
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.target instanceof HTMLElement && e.target.matches('input, select, textarea')) return;
      switch (playerKeyAction(e.key)) {
        case 'togglePlay':
          e.preventDefault();
          show.togglePlaying();
          break;
        case 'next':
          show.next();
          break;
        case 'previous':
          show.previous();
          break;
        case 'first':
          show.first();
          break;
        case 'last':
          show.last();
          break;
        case 'fullscreen':
          fullscreen.toggle();
          break;
        case 'exit':
          // 全螢幕時 Esc 被瀏覽器收走去離開全螢幕，這裡只處理非全螢幕的情況。
          if (!document.fullscreenElement) onExit();
          break;
        case 'slower':
          onSettingsChange({ interval: clampInterval(settings.interval + 1) });
          break;
        case 'faster':
          onSettingsChange({ interval: clampInterval(settings.interval - 1) });
          break;
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [show, fullscreen, onExit, onSettingsChange, settings.interval]);

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
      <PhotoLayers
        layers={show.layers}
        name={show.current?.name ?? ''}
        fit={settings.fit}
        unplayable={show.unplayable}
        onTogglePlay={show.togglePlaying}
      />

      <TopBar
        idle={idle}
        path={show.current?.path}
        position={show.index + 1}
        total={show.total}
        isFullscreen={fullscreen.isFullscreen}
        onExit={onExit}
        onToggleFullscreen={fullscreen.toggle}
      />

      <ControlBar
        idle={idle}
        playing={show.playing}
        settings={settings}
        onPrevious={show.previous}
        onTogglePlay={show.togglePlaying}
        onNext={show.next}
        onSettingsChange={onSettingsChange}
        onToggleShuffle={show.toggleShuffle}
      />

      {/* 進度條。key 讓它每張重新掛載，animation 才會從 0 重跑。 */}
      {show.playing && (
        <Box
          key={`${show.index}-${settings.interval}`}
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
