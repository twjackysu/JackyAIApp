import FolderOpenIcon from '@mui/icons-material/FolderOpen';
import { Alert, Box, Button, Card, CardContent, Stack, Typography } from '@mui/material';
import { useCallback, useRef, useState } from 'react';

import { collectPhotos, type PhotoFile } from './photoFiles';
import { loadSettings, saveSettings, type SlideshowSettings } from './settings';
import SlideshowPlayer from './SlideshowPlayer';

/**
 * 資料夾照片輪播。
 *
 * 為什麼要做成網頁而不是留在單機 HTML：用 `file://` 開的靜態頁面沒辦法列出資料夾內容，
 * 只能拿 `new Image()` 一個一個去試 `1.jpg`、`2.jpg`… 看載不載得起來，所以檔名被迫是連號
 * 數字。瀏覽器的資料夾選擇器（`webkitdirectory`）會直接把整份檔案清單交給網頁，檔名和副
 * 檔名都是真的，什麼名字都能播。
 *
 * 照片不會離開這台電腦：`File` 物件只是個 handle，這裡只用 `URL.createObjectURL` 在本頁
 * 顯示，沒有任何一行程式把它送去後端。整個功能沒有呼叫任何 API。
 */
function Slideshow() {
  const [photos, setPhotos] = useState<PhotoFile[] | null>(null);
  const [folderName, setFolderName] = useState('');
  const [skipped, setSkipped] = useState(0);
  const [playing, setPlaying] = useState(false);
  const [settings, setSettings] = useState<SlideshowSettings>(loadSettings);
  const inputRef = useRef<HTMLInputElement>(null);

  const updateSettings = useCallback((patch: Partial<SlideshowSettings>) => {
    setSettings((prev) => {
      const next = { ...prev, ...patch };
      saveSettings(next);
      return next;
    });
  }, []);

  const handlePick = (e: React.ChangeEvent<HTMLInputElement>) => {
    const all = Array.from(e.target.files ?? []);
    // 清掉 value，否則再選同一個資料夾（例如剛加了幾張照片進去）瀏覽器會認為值沒變，
    // 不觸發 change，看起來就像按鈕壞了。File 物件已經取出來了，清掉不影響它們。
    e.target.value = '';
    if (all.length === 0) return;

    const found = collectPhotos(all);
    setPhotos(found);
    setSkipped(all.length - found.length);
    // webkitRelativePath 的第一段就是使用者選的那層資料夾名（拿不到完整路徑，瀏覽器不給）。
    setFolderName(all[0].webkitRelativePath?.split('/')[0] ?? '');
    setPlaying(found.length > 0);
  };

  if (playing && photos && photos.length > 0) {
    return (
      <SlideshowPlayer
        // 換資料夾時整個重建，播放位置和 blob URL 才不會延續到新清單上。
        key={`${folderName}-${photos.length}`}
        photos={photos}
        settings={settings}
        onSettingsChange={updateSettings}
        onExit={() => setPlaying(false)}
      />
    );
  }

  return (
    <Box sx={{ maxWidth: 560, mx: 'auto', mt: 4 }}>
      <Card variant="outlined">
        <CardContent>
          <Typography variant="h6" gutterBottom>
            資料夾照片輪播
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2.5 }}>
            選一個資料夾，直接播放裡面的照片。檔名不限，子資料夾裡的也會一起播。
          </Typography>

          {/* webkitdirectory 沒有對應的 JSX 屬性，只能在 ref 上設 */}
          <input
            ref={(el) => {
              inputRef.current = el;
              if (el) el.webkitdirectory = true;
            }}
            type="file"
            style={{ display: 'none' }}
            onChange={handlePick}
          />

          <Stack spacing={2}>
            <Button
              variant="contained"
              size="large"
              startIcon={<FolderOpenIcon />}
              onClick={() => inputRef.current?.click()}
            >
              選擇資料夾
            </Button>

            {photos && (
              <Alert
                severity={photos.length > 0 ? 'success' : 'warning'}
                action={
                  photos.length > 0 ? (
                    <Button color="inherit" size="small" onClick={() => setPlaying(true)}>
                      播放
                    </Button>
                  ) : undefined
                }
              >
                {photos.length > 0
                  ? `${folderName || '資料夾'}：找到 ${photos.length} 張照片` +
                    (skipped > 0 ? `（略過 ${skipped} 個非圖片檔）` : '')
                  : '這個資料夾裡沒有找得到的圖片檔。'}
              </Alert>
            )}

            <Typography variant="caption" color="text.secondary">
              照片依檔名順序播放，播放中可以勾選「隨機播放」。瀏覽器不會記住選過的資料夾，
              每次進來要重選一次。
              <br />
              快捷鍵：空白鍵 播放/暫停、← → 上下張、F 全螢幕、+ − 調秒數、Esc 離開。
            </Typography>
          </Stack>
        </CardContent>
      </Card>
    </Box>
  );
}

export default Slideshow;
