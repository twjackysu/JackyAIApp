import UploadFileIcon from '@mui/icons-material/UploadFile';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Paper from '@mui/material/Paper';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import { useRef, useState } from 'react';

import { ParsedImportRow, parseImportRow } from './csvImport';

import { PositionInput } from '@/apis/portfolioApis/types';
import { parseTabular } from '@/utils/tabularParse';

interface Props {
  open: boolean;
  onClose: () => void;
  onImport: (positions: PositionInput[]) => void;
}

const PREVIEW_LIMIT = 5;

export function ImportCsvDialog({ open, onClose, onImport }: Props) {
  const [parsed, setParsed] = useState<ParsedImportRow[]>([]);
  const [parseError, setParseError] = useState('');
  const fileRef = useRef<HTMLInputElement>(null);

  const validRows = parsed.filter((r) => r.position !== null);
  const invalidCount = parsed.length - validRows.length;

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setParseError('');
    const reader = new FileReader();
    reader.onload = (ev) => {
      const text = ev.target?.result as string;
      const result = parseTabular(text);
      if (!result) {
        setParseError('無法解析檔案 — 請確認是含標題列的有效 CSV 檔案。');
        setParsed([]);
        return;
      }
      setParsed(result.rows.map(parseImportRow));
    };
    reader.readAsText(file, 'UTF-8');
  };

  const handleAdd = () => {
    onImport(validRows.map((r) => r.position as PositionInput));
    handleClose();
  };

  const handleClose = () => {
    setParsed([]);
    setParseError('');
    if (fileRef.current) fileRef.current.value = '';
    onClose();
  };

  const previewRows = parsed.slice(0, PREVIEW_LIMIT);

  return (
    <Dialog open={open} onClose={handleClose} maxWidth="sm" fullWidth>
      <DialogTitle>匯入 CSV</DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
        <Typography variant="body2" color="text.secondary">
          選擇符合匯入範本欄位的 CSV 檔案（可於「下載匯入範本」取得），預覽解析結果後點擊 「加入」—
          仍需在下方按「儲存快照」才會實際儲存，匯入前可先修改。
        </Typography>

        <Button
          variant="outlined"
          component="label"
          startIcon={<UploadFileIcon />}
          sx={{ alignSelf: 'flex-start' }}
        >
          選擇 CSV 檔案
          <input hidden type="file" accept=".csv" ref={fileRef} onChange={handleFileChange} />
        </Button>

        {parseError && <Alert severity="error">{parseError}</Alert>}

        {parsed.length > 0 && (
          <>
            <Typography variant="body2">
              共 {parsed.length} 筆，{validRows.length} 筆有效
              {invalidCount > 0 && `，${invalidCount} 筆略過，欄位有誤`}
            </Typography>
            <TableContainer component={Paper} variant="outlined" sx={{ maxHeight: 240 }}>
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
                  {previewRows.map((r, i) => (
                    <TableRow key={i}>
                      <TableCell>{r.raw['類型']}</TableCell>
                      <TableCell>{r.raw['代號']}</TableCell>
                      <TableCell>{r.position?.name || r.raw['名稱']}</TableCell>
                      <TableCell>{r.raw['數量']}</TableCell>
                      <TableCell>
                        {r.error ? (
                          <Typography variant="caption" color="error">
                            {r.error}
                          </Typography>
                        ) : (
                          <Typography variant="caption" color="success.main">
                            有效
                          </Typography>
                        )}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          </>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={handleClose}>取消</Button>
        <Button variant="contained" disabled={validRows.length === 0} onClick={handleAdd}>
          加入 ({validRows.length})
        </Button>
      </DialogActions>
    </Dialog>
  );
}
