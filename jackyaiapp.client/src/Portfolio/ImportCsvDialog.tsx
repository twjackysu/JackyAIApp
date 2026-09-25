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

import { buildImportPreview, ImportPreviewRow } from './csvImport';
import { EditorRow } from './editorTypes';

import { parseTabular } from '@/utils/tabularParse';

interface Props {
  onImport: (rows: EditorRow[]) => void;
}

const PREVIEW_LIMIT = 10;

export function ImportCsvDialog({ onImport }: Props) {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const [open, setOpen] = useState(false);
  const [preview, setPreview] = useState<ImportPreviewRow[] | null>(null);
  const [parseError, setParseError] = useState('');
  const fileRef = useRef<HTMLInputElement>(null);

  const validRows = (preview ?? []).filter((p) => p.editorRow).map((p) => p.editorRow as EditorRow);
  const invalidCount = (preview ?? []).length - validRows.length;

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setParseError('');
    const reader = new FileReader();
    reader.onload = (ev) => {
      const text = ev.target?.result as string;
      const parsed = parseTabular(text);
      if (!parsed) {
        setParseError('無法解析檔案 — 請確認是含標題列的有效 CSV 檔案。');
        setPreview(null);
        return;
      }
      setPreview(buildImportPreview(parsed.rows));
    };
    reader.readAsText(file, 'UTF-8');
  };

  const handleClose = () => {
    setOpen(false);
    setPreview(null);
    setParseError('');
    if (fileRef.current) fileRef.current.value = '';
  };

  const handleAdd = () => {
    if (validRows.length === 0) return;
    onImport(validRows);
    handleClose();
  };

  return (
    <>
      <Button
        size="small"
        variant="outlined"
        startIcon={<UploadFileIcon fontSize="small" />}
        onClick={() => setOpen(true)}
      >
        匯入 CSV
      </Button>

      <Dialog open={open} onClose={handleClose} maxWidth="sm" fullWidth fullScreen={fullScreen}>
        <DialogTitle>匯入 CSV</DialogTitle>
        <DialogContent
          sx={{ display: 'flex', flexDirection: 'column', gap: 2, pt: '16px !important' }}
        >
          <Typography variant="body2" color="text.secondary">
            選擇符合匯入範本欄位的 CSV
            檔案。匯入的項目會加入下方編輯區，仍需按「儲存快照」才會真正儲存。
          </Typography>

          <Box>
            <Button variant="outlined" component="label" startIcon={<UploadFileIcon />}>
              選擇 CSV 檔案
              <input hidden type="file" accept=".csv" ref={fileRef} onChange={handleFileChange} />
            </Button>
          </Box>

          {parseError && <Alert severity="error">{parseError}</Alert>}

          {preview && !parseError && (
            <Box>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
                偵測到 {preview.length} 列，{validRows.length} 筆可匯入
                {invalidCount > 0 && `，${invalidCount} 筆略過，欄位有誤`}。
              </Typography>
              <TableContainer component={Paper} variant="outlined" sx={{ maxHeight: 260 }}>
                <Table size="small" stickyHeader>
                  <TableHead>
                    <TableRow>
                      <TableCell>類型</TableCell>
                      <TableCell>代號</TableCell>
                      <TableCell>名稱</TableCell>
                      <TableCell>數量</TableCell>
                      <TableCell>狀態</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {preview.slice(0, PREVIEW_LIMIT).map((p, i) => (
                      <TableRow key={i}>
                        <TableCell>{p.raw['類型']}</TableCell>
                        <TableCell>{p.raw['代號']}</TableCell>
                        <TableCell>{p.raw['名稱']}</TableCell>
                        <TableCell>{p.raw['數量']}</TableCell>
                        <TableCell>
                          {p.editorRow ? (
                            <Typography variant="caption" color="success.main">
                              可匯入
                            </Typography>
                          ) : (
                            <Typography variant="caption" color="error.main">
                              {p.error}
                            </Typography>
                          )}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
              {preview.length > PREVIEW_LIMIT && (
                <Typography variant="caption" color="text.secondary">
                  僅顯示前 {PREVIEW_LIMIT} 列，其餘 {preview.length - PREVIEW_LIMIT} 列不影響匯入。
                </Typography>
              )}
            </Box>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={handleClose}>取消</Button>
          <Button variant="contained" onClick={handleAdd} disabled={validRows.length === 0}>
            加入 ({validRows.length})
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
