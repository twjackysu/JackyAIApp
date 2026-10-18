import { readJson, writeJson } from '@/utils/storage';

export interface SlideshowSettings {
  /** 每張停留秒數。 */
  interval: number;
  loop: boolean;
  /** 隨機播放，預設開啟。使用者取消勾選才照檔名順序播。 */
  shuffle: boolean;
  /** contain = 完整顯示；cover = 填滿裁切。 */
  fit: 'contain' | 'cover';
}

export const DEFAULT_SETTINGS: SlideshowSettings = {
  interval: 5,
  loop: true,
  shuffle: true,
  fit: 'contain',
};

const STORAGE_KEY = 'slideshow-settings';

/**
 * 只存偏好設定，資料夾本身存不了 —— 瀏覽器不會把使用者選的路徑交給網頁，所以每次進來
 * 都得重選一次資料夾。這是沙箱的規則，不是漏做。
 */
export function loadSettings(): SlideshowSettings {
  // readJson 在無痕視窗、擋掉 site data（localStorage 直接丟例外）、或內容不是合法 JSON 時回 null：
  // 設定記不住無所謂，但不該因此讓整頁掛掉。
  const saved = readJson<Partial<SlideshowSettings>>(STORAGE_KEY);
  if (saved === null || typeof saved !== 'object') return DEFAULT_SETTINGS;
  // 使用者可能存過舊版本的形狀，逐欄位挑出來而不是整包信任。
  return {
    interval:
      typeof saved.interval === 'number' && saved.interval >= 1 && saved.interval <= 30
        ? saved.interval
        : DEFAULT_SETTINGS.interval,
    loop: typeof saved.loop === 'boolean' ? saved.loop : DEFAULT_SETTINGS.loop,
    // 舊版的 sort 欄位不再理會：那時每次存設定都會連同 sort: 'name'（預設值）一起寫入，
    // 看不出使用者是否真的選過檔名順序。拿它來推 shuffle 會讓舊設定把新預設蓋成不隨機。
    shuffle: typeof saved.shuffle === 'boolean' ? saved.shuffle : DEFAULT_SETTINGS.shuffle,
    fit: saved.fit === 'cover' ? 'cover' : 'contain',
  };
}

export function saveSettings(settings: SlideshowSettings): void {
  writeJson(STORAGE_KEY, settings);
}
