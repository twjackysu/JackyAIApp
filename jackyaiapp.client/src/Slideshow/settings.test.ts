import { afterEach, describe, expect, it, vi } from 'vitest';

import { DEFAULT_SETTINGS, loadSettings, saveSettings } from './settings';

const KEY = 'slideshow-settings';

describe('loadSettings', () => {
  afterEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  it('第一次進來預設就是隨機播放', () => {
    expect(loadSettings().shuffle).toBe(true);
  });

  it('使用者取消過隨機，下次進來維持檔名順序', () => {
    saveSettings({ ...DEFAULT_SETTINGS, shuffle: false });

    expect(loadSettings().shuffle).toBe(false);
  });

  it('舊版存的 sort 欄位不會把預設蓋成不隨機', () => {
    // 舊版每次存設定都會寫入 sort: 'name'（它的預設值），那不代表使用者選過檔名順序。
    localStorage.setItem(
      KEY,
      JSON.stringify({ interval: 8, loop: true, fit: 'contain', sort: 'name' }),
    );

    const loaded = loadSettings();

    expect(loaded.shuffle).toBe(true);
    expect(loaded.interval).toBe(8); // 其他舊設定照樣沿用
  });

  it('存壞的內容退回預設，不讓整頁掛掉', () => {
    localStorage.setItem(KEY, '{not json');

    expect(loadSettings()).toEqual(DEFAULT_SETTINGS);
  });

  it('localStorage 本身丟例外（無痕視窗、擋掉 site data）時也退回預設', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('SecurityError');
    });

    expect(loadSettings()).toEqual(DEFAULT_SETTINGS);
  });

  it('超出範圍的秒數不採用', () => {
    localStorage.setItem(KEY, JSON.stringify({ interval: 999 }));

    expect(loadSettings().interval).toBe(DEFAULT_SETTINGS.interval);
  });
});
