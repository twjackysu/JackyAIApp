import FolderOpenIcon from '@mui/icons-material/FolderOpen';
import LockIcon from '@mui/icons-material/Lock';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material';
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
    if (all.length === 0) return;

    const found = collectPhotos(all, settings.sort);
    setPhotos(found);
    setSkipped(all.length - found.length);
    // webkitRelativePath 的第一段就是使用者選的那層資料夾名（拿不到完整路徑，瀏覽器不給）。
    setFolderName(all[0].webkitRelativePath?.split('/')[0] ?? '');
    setPlaying(found.length > 0);
  };

  // 換排序時就地重排已經選好的清單，不用叫使用者再選一次資料夾。
  const handleSortChange = (sort: SlideshowSettings['sort']) => {
    updateSettings({ sort });
    if (photos)
      setPhotos(
        collectPhotos(
          photos.map((p) => p.file),
          sort,
        ),
      );
  };

  if (playing && photos && photos.length > 0) {
    return (
      <SlideshowPlayer
        // 換資料夾或換排序時整個重建，播放位置和 blob URL 才不會延續到新清單上。
        key={`${folderName}-${settings.sort}-${photos.length}`}
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
            選一個資料夾就開始播，檔名不限。子資料夾也會一起收進來。
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

            <TextField
              select
              size="small"
              label="排序"
              value={settings.sort}
              onChange={(e) => handleSortChange(e.target.value as SlideshowSettings['sort'])}
            >
              <MenuItem value="name">檔名（1 排在 10 前面）</MenuItem>
              <MenuItem value="modified">修改時間</MenuItem>
              <MenuItem value="random">隨機</MenuItem>
            </TextField>

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

            <Box sx={{ display: 'flex', gap: 1, color: 'text.secondary' }}>
              <LockIcon fontSize="small" sx={{ mt: 0.25 }} />
              <Typography variant="caption">
                照片不會離開這台電腦。瀏覽器只把檔案清單交給這個頁面，圖片直接在本機讀取顯示，
                沒有任何內容被上傳。也因為同樣的沙箱規則，網頁拿不到完整路徑、也記不住上次選的
                資料夾，每次進來要重選一次。
              </Typography>
            </Box>

            <Typography variant="caption" color="text.secondary">
              播放中：空白鍵 播放/暫停、← → 上下張、F 全螢幕、+ − 調秒數、Esc 離開。
            </Typography>
          </Stack>
        </CardContent>
      </Card>
    </Box>
  );
}

export default Slideshow;
