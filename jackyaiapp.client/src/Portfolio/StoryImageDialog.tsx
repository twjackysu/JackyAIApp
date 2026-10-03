import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogContentText from '@mui/material/DialogContentText';
import DialogTitle from '@mui/material/DialogTitle';
import { useEffect, useState } from 'react';

import { StoryAllocation } from './storyAllocation';
import { canvasToPngBlob, renderStoryImage } from './storyImage';

interface Props {
  open: boolean;
  onClose: () => void;
  alloc: StoryAllocation;
  /** ISO yyyy-mm-dd shown on the image and used in the filename. */
  date: string;
}

/** Previews the IG-story PNG and hands it to the phone's share sheet, or downloads it. */
export function StoryImageDialog({ open, onClose, alloc, date }: Props) {
  const [blob, setBlob] = useState<Blob | null>(null);
  const [url, setUrl] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    let objectUrl: string | null = null;
    setError(null);
    renderStoryImage(alloc, date.replace(/-/g, '.'))
      .then(canvasToPngBlob)
      .then((b) => {
        if (cancelled) return;
        objectUrl = URL.createObjectURL(b);
        setBlob(b);
        setUrl(objectUrl);
      })
      .catch((e: unknown) => !cancelled && setError(e instanceof Error ? e.message : String(e)));
    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
      setBlob(null);
      setUrl(null);
    };
  }, [open, alloc, date]);

  const filename = `portfolio_story_${date}.png`;
  const file = blob ? new File([blob], filename, { type: 'image/png' }) : null;
  const canShare = !!file && !!navigator.canShare?.({ files: [file] });

  const download = () => {
    if (!url) return;
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    a.remove();
  };

  const share = async () => {
    if (!file) return;
    try {
      await navigator.share({ files: [file] });
    } catch (e) {
      // AbortError = the user closed the share sheet; anything else, fall back to a download.
      if (!(e instanceof DOMException && e.name === 'AbortError')) download();
    }
  };

  return (
    <Dialog open={open} onClose={onClose} maxWidth="xs" fullWidth>
      <DialogTitle>IG 限動圖</DialogTitle>
      <DialogContent>
        <DialogContentText sx={{ mb: 2 }}>
          只顯示台股、美股各自的持股比例，不含金額。台股在上半、美股在下半，各自加總 100%，
          不想放美股的話直接裁掉下半部即可。
        </DialogContentText>
        {error && <Alert severity="error">產圖失敗：{error}</Alert>}
        <Box
          sx={{
            aspectRatio: '9 / 16',
            maxHeight: '60vh',
            mx: 'auto',
            borderRadius: 2,
            overflow: 'hidden',
            bgcolor: '#0B0F1E',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
          }}
        >
          {url ? (
            <Box
              component="img"
              src={url}
              alt="持股配置限動圖"
              sx={{ width: '100%', height: '100%', display: 'block' }}
            />
          ) : (
            !error && <CircularProgress />
          )}
        </Box>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>關閉</Button>
        <Button onClick={download} disabled={!url}>
          下載 PNG
        </Button>
        {canShare && (
          <Button variant="contained" onClick={share}>
            分享
          </Button>
        )}
      </DialogActions>
    </Dialog>
  );
}
