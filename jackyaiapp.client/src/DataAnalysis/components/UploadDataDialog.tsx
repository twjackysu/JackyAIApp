import AccessTimeIcon from '@mui/icons-material/AccessTime';
import CloudUploadIcon from '@mui/icons-material/CloudUpload';
import ContentPasteIcon from '@mui/icons-material/ContentPaste';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Paper from '@mui/material/Paper';
import { useTheme } from '@mui/material/styles';
import Tab from '@mui/material/Tab';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Tabs from '@mui/material/Tabs';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import useMediaQuery from '@mui/material/useMediaQuery';
import { useRef, useState } from 'react';

import { useUploadSessionMutation } from '../../apis/dataAnalysisApis';

import type { SessionInfo } from '../../apis/dataAnalysisApis/types';

import { fmtNum } from '@/utils/format';
import { parseTabular } from '@/utils/tabularParse';

interface UploadDataDialogProps {
  open: boolean;
  onClose: () => void;
  onSessionCreated: (info: SessionInfo) => void;
}

// ── Component ─────────────────────────────────────────────────────────────────

export function UploadDataDialog({ open, onClose, onSessionCreated }: UploadDataDialogProps) {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const [tab, setTab] = useState(0); // 0 = paste, 1 = file
  const [pasteText, setPaste] = useState('');
  const [datasetName, setName] = useState('');
  const [parsed, setParsed] = useState<{
    headers: string[];
    rows: Record<string, string>[];
  } | null>(null);
  const [parseError, setErr] = useState('');
  const fileRef = useRef<HTMLInputElement>(null);

  const [upload, { isLoading }] = useUploadSessionMutation();

  const handlePasteChange = (text: string) => {
    setPaste(text);
    setErr('');
    if (!text.trim()) {
      setParsed(null);
      return;
    }
    const result = parseTabular(text);
    if (!result) {
      setErr('無法偵測欄位 — 請確認第一列包含欄位標題。');
      setParsed(null);
    } else setParsed(result);
  };

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setErr('');
    const reader = new FileReader();
    reader.onload = (ev) => {
      const text = ev.target?.result as string;
      const result = parseTabular(text);
      if (!result) setErr('無法解析檔案 — 請確認是含標題列的有效 CSV/TSV 檔案。');
      else {
        setParsed(result);
        if (!datasetName) setName(file.name.replace(/\.[^.]+$/, ''));
      }
    };
    reader.readAsText(file, 'UTF-8');
  };

  const handleSubmit = async () => {
    if (!parsed || parsed.rows.length === 0) return;
    try {
      const res = await upload({
        name: datasetName.trim() || undefined,
        rows: parsed.rows,
      }).unwrap();

      if (res.data) {
        onSessionCreated(res.data);
        handleClose();
      }
    } catch {
      setErr('上傳失敗 — 請再試一次。');
    }
  };

  const handleClose = () => {
    setPaste('');
    setParsed(null);
    setErr('');
    setName('');
    setTab(0);
    onClose();
  };

  const previewRows = parsed?.rows.slice(0, 5) ?? [];

  return (
    <Dialog open={open} onClose={handleClose} maxWidth="md" fullWidth fullScreen={fullScreen}>
      <DialogTitle sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        <CloudUploadIcon fontSize="small" />
        上傳資料
        <Chip
          icon={<AccessTimeIcon />}
          label="暫存 — 2 小時"
          color="warning"
          size="small"
          sx={{ ml: 1 }}
        />
      </DialogTitle>
      <DialogContent>
        <Alert severity="warning" sx={{ mb: 2 }}>
          <strong>僅暫存於此次連線。</strong>
          此資料儲存在伺服器記憶體中，2 小時後會自動刪除，不會存進任何資料庫。
        </Alert>

        <TextField
          label="資料集名稱（選填）"
          size="small"
          fullWidth
          value={datasetName}
          onChange={(e) => setName(e.target.value)}
          placeholder="例如：Asset Trend 2025"
          sx={{ mb: 2 }}
        />

        <Tabs value={tab} onChange={(_, v: number) => setTab(v)} sx={{ mb: 2 }}>
          <Tab icon={<ContentPasteIcon />} iconPosition="start" label="從 Excel 貼上" />
          <Tab icon={<CloudUploadIcon />} iconPosition="start" label="上傳 CSV / TSV" />
        </Tabs>

        {tab === 0 && (
          <TextField
            multiline
            rows={8}
            fullWidth
            size="small"
            placeholder={
              '在此貼上 Excel 資料（複製儲存格時請包含標題列）：\n\n' +
              'Date\tAsset\tValue\n2025-01\tStocks\t100000\n2025-02\tStocks\t105000'
            }
            value={pasteText}
            onChange={(e) => handlePasteChange(e.target.value)}
            slotProps={{
              htmlInput: { style: { fontFamily: 'monospace', fontSize: 12 } },
            }}
          />
        )}

        {tab === 1 && (
          <Box>
            <Button variant="outlined" component="label" startIcon={<CloudUploadIcon />}>
              選擇 CSV / TSV 檔案
              <input
                hidden
                type="file"
                accept=".csv,.tsv,.txt"
                ref={fileRef}
                onChange={handleFileChange}
              />
            </Button>
          </Box>
        )}

        {parseError && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {parseError}
          </Alert>
        )}

        {parsed && !parseError && (
          <Box sx={{ mt: 2 }}>
            <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
              偵測到 <strong>{fmtNum(parsed.rows.length)}</strong> 列 ×{' '}
              <strong>{parsed.headers.length}</strong> 欄 — 預覽（前5列）：
            </Typography>
            <TableContainer component={Paper} variant="outlined" sx={{ maxHeight: 220 }}>
              <Table size="small" stickyHeader>
                <TableHead>
                  <TableRow>
                    {parsed.headers.map((h) => (
                      <TableCell key={h} sx={{ fontWeight: 600, whiteSpace: 'nowrap' }}>
                        {h}
                      </TableCell>
                    ))}
                  </TableRow>
                </TableHead>
                <TableBody>
                  {previewRows.map((row, i) => (
                    <TableRow key={i}>
                      {parsed.headers.map((h) => (
                        <TableCell
                          key={h}
                          sx={{
                            whiteSpace: 'nowrap',
                            maxWidth: 160,
                            overflow: 'hidden',
                            textOverflow: 'ellipsis',
                          }}
                        >
                          {row[h]}
                        </TableCell>
                      ))}
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          </Box>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={handleClose} disabled={isLoading}>
          取消
        </Button>
        <Button
          variant="contained"
          onClick={handleSubmit}
          disabled={!parsed || parsed.rows.length === 0 || isLoading}
          startIcon={isLoading ? <CircularProgress size={16} /> : <CloudUploadIcon />}
        >
          {isLoading ? '上傳中…' : '載入到連線'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
