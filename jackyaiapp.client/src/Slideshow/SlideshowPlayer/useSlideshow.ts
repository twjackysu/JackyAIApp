import { useCallback, useEffect, useMemo, useRef, useState } from 'react';

import { ObjectUrlWindow } from '../objectUrlWindow';
import { shuffledOrder } from '../photoFiles';

import type { PhotoFile } from '../photoFiles';
import type { SlideshowSettings } from '../settings';

/**
 * Which photo is on screen and what plays next: the play order (file order, or shuffled), moving
 * through it and playing on by itself, the two layers the photos cross-fade between, and skipping
 * a photo the browser cannot decode.
 */
export function useSlideshow(
  photos: PhotoFile[],
  settings: SlideshowSettings,
  onSettingsChange: (patch: Partial<SlideshowSettings>) => void,
) {
  const [index, setIndex] = useState(0);
  const [playing, setPlaying] = useState(true);

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

  return {
    index,
    total,
    /** The photo on screen. */
    current: photos[order[index]] as PhotoFile | undefined,
    layers,
    unplayable,
    playing,
    togglePlaying: () => setPlaying((p) => !p),
    next: () => go(index + 1, 1),
    previous: () => go(index - 1, -1),
    first: () => go(0, 1),
    last: () => go(total - 1, -1),
    toggleShuffle,
  };
}
