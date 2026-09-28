export interface SlideshowSettings {
  /** 每張停留秒數。 */
  interval: number;
  loop: boolean;
  /** 隨機播放。關掉時照檔名順序播。 */
  shuffle: boolean;
  /** contain = 完整顯示；cover = 填滿裁切。 */
  fit: 'contain' | 'cover';
}

export const DEFAULT_SETTINGS: SlideshowSettings = {
  interval: 5,
  loop: true,
  shuffle: false,
  fit: 'contain',
};

const STORAGE_KEY = 'slideshow-settings';

/**
 * 只存偏好設定，資料夾本身存不了 —— 瀏覽器不會把使用者選的路徑交給網頁，所以每次進來
 * 都得重選一次資料夾。這是沙箱的規則，不是漏做。
 */
export function loadSettings(): SlideshowSettings {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return DEFAULT_SETTINGS;
    // 使用者可能存過舊版本的形狀（例如舊版的 sort 欄位），逐欄位挑出來而不是整包信任。
    const saved = JSON.parse(raw) as Partial<SlideshowSettings> & { sort?: unknown };
    return {
      interval:
        typeof saved.interval === 'number' && saved.interval >= 1 && saved.interval <= 30
          ? saved.interval
          : DEFAULT_SETTINGS.interval,
      loop: typeof saved.loop === 'boolean' ? saved.loop : DEFAULT_SETTINGS.loop,
      // 舊版把隨機當成一種排序（sort: 'random'），沿用那個選擇而不是悄悄關掉。
      shuffle: typeof saved.shuffle === 'boolean' ? saved.shuffle : saved.sort === 'random',
      fit: saved.fit === 'cover' ? 'cover' : 'contain',
    };
  } catch {
    // 無痕視窗、擋掉 site data 時 localStorage 會直接丟例外。設定記不住無所謂，
    // 但不該因此讓整頁掛掉。
    return DEFAULT_SETTINGS;
  }
}

export function saveSettings(settings: SlideshowSettings): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(settings));
  } catch {
    /* 同上：存不了就算了。 */
  }
}
