import DownloadIcon from '@mui/icons-material/Download';
import UploadFileIcon from '@mui/icons-material/UploadFile';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Paper from '@mui/material/Paper';
import { useTheme } from '@mui/material/styles';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import useMediaQuery from '@mui/material/useMediaQuery';
import { useRef, useState } from 'react';

import { downloadImportTemplate, ParsedImportCsv, parseImportCsv } from './csvImport';

import { PositionInput } from '@/apis/portfolioApis/types';

const PREVIEW_LIMIT = 20;

interface Props {
  onImport: (positions: PositionInput[]) => void;
}

/**
 * "匯入 CSV" entry point for `HoldingsEditor`'s update dialog. Only populates the editor's row
 * state via `onImport` — it never saves directly, so a bad import is always reviewable/editable
 * behind the existing "儲存快照" flow. See ARCHITECTURE.md → "Decision: Portfolio CSV import for
 * holdings — Gap Audit item 2".
 */
export function HoldingsImportDialog({ onImport }: Props) {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const [open, setOpen] = useState(false);
  const [parsed, setParsed] = useState<ParsedImportCsv | null>(null);
  const [fileError, setFileError] = useState('');
  const fileRef = useRef<HTMLInputElement>(null);

  const validResults = parsed?.results.filter((r) => r.error === null) ?? [];
  const invalidCount = (parsed?.results.length ?? 0) - validResults.length;

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setFileError('');
    const reader = new FileReader();
    reader.onload = (ev) => {
      const text = ev.target?.result as string;
      const result = parseImportCsv(text);
      if (!result) {
        setFileError('無法解析檔案 — 請確認是含標題列的有效 CSV 檔案。');
        setParsed(null);
      } else {
        setParsed(result);
      }
    };
    reader.readAsText(file, 'UTF-8');
  };

  const handleImport = () => {
    if (validResults.length === 0) return;
    onImport(validResults.map((r) => r.position as PositionInput));
    handleClose();
  };

  const handleClose = () => {
    setParsed(null);
    setFileError('');
    if (fileRef.current) fileRef.current.value = '';
    setOpen(false);
  };

  return (
    <>
      <Box sx={{ display: 'flex', gap: 1 }}>
        <Button size="small" startIcon={<UploadFileIcon />} onClick={() => setOpen(true)}>
          匯入 CSV
        </Button>
        <Button size="small" startIcon={<DownloadIcon />} onClick={downloadImportTemplate}>
          下載匯入範本
        </Button>
      </Box>

      <Dialog open={open} onClose={handleClose} maxWidth="sm" fullWidth fullScreen={fullScreen}>
        <DialogTitle>匯入 CSV</DialogTitle>
        <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
          <Typography variant="body2" color="text.secondary">
            選擇符合「下載匯入範本」欄位格式的 CSV
            檔案。匯入的項目會先加入下方編輯列表，仍需按下「儲存快照」才會實際存檔。
          </Typography>

          <Button variant="outlined" component="label" startIcon={<UploadFileIcon />}>
            選擇 CSV 檔案
            <input hidden type="file" accept=".csv" ref={fileRef} onChange={handleFileChange} />
          </Button>

          {fileError && <Alert severity="error">{fileError}</Alert>}

          {parsed && !fileError && (
            <Box>
              <Typography variant="body2" sx={{ mb: 1 }}>
                共 {parsed.results.length} 列，{validResults.length} 筆可匯入
                {invalidCount > 0 && `，${invalidCount} 筆略過，欄位有誤`}
              </Typography>
              <TableContainer component={Paper} variant="outlined" sx={{ maxHeight: 260 }}>
                <Table size="small" stickyHeader>
                  <TableHead>
                    <TableRow>
                      <TableCell>類型</TableCell>
                      <TableCell>代號/名稱</TableCell>
                      <TableCell>數量</TableCell>
                      <TableCell>狀態</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {parsed.results.slice(0, PREVIEW_LIMIT).map((r, i) => (
                      <TableRow key={i}>
                        <TableCell>{r.row['類型']}</TableCell>
                        <TableCell sx={{ whiteSpace: 'nowrap' }}>
                          {[r.row['代號'], r.row['名稱']].filter(Boolean).join(' ') || '—'}
                        </TableCell>
                        <TableCell>{r.row['數量']}</TableCell>
                        <TableCell>
                          {r.error ? (
                            <Typography variant="caption" color="error">
                              {r.error}
                            </Typography>
                          ) : (
                            <Typography variant="caption" color="success.main">
                              可匯入
                            </Typography>
                          )}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
              {parsed.results.length > PREVIEW_LIMIT && (
                <Typography variant="caption" color="text.secondary">
                  僅顯示前 {PREVIEW_LIMIT} 列，其餘會一併計入匯入結果
                </Typography>
              )}
            </Box>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={handleClose}>取消</Button>
          <Button variant="contained" onClick={handleImport} disabled={validResults.length === 0}>
            加入 {validResults.length > 0 ? `(${validResults.length})` : ''}
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
