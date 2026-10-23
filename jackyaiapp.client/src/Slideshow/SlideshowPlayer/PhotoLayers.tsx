import { Box, Typography } from '@mui/material';

import type { SlideshowSettings } from '../settings';

interface PhotoLayersProps {
  layers: { prev: string | null; cur: string | null };
  /** The name of the photo on screen. */
  name: string;
  fit: SlideshowSettings['fit'];
  unplayable: boolean;
  onTogglePlay: () => void;
}

/** The photo, cross-faded in over the one before it; a click on it plays or pauses. */
export function PhotoLayers({ layers, name, fit, unplayable, onTogglePlay }: PhotoLayersProps) {
  return (
    <Box
      onClick={(e) => {
        // 點畫面本身播放/暫停；點控制列上的按鈕不算。
        if (e.target === e.currentTarget) onTogglePlay();
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
              alt={i === 1 ? name : ''}
              sx={{
                position: 'absolute',
                inset: 0,
                width: '100%',
                height: '100%',
                objectFit: fit,
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
  );
}
