import UploadFileIcon from '@mui/icons-material/UploadFile';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
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
import { useState } from 'react';

import { CsvImportResult, mapCsvRows } from './csvImport';

import { PositionInput } from '@/apis/portfolioApis/types';
import { parseTabular } from '@/utils/tabularParse';

interface Props {
  open: boolean;
  onClose: () => void;
  onImport: (positions: PositionInput[]) => void;
}

/**
 * File-picker sub-dialog for bulk-importing holdings: parse a `.csv` via the shared tabular
 * parser, map each row to a `PositionInput` (see `csvImport.ts`), preview the split between
 * valid/invalid rows, then "加入" appends only the valid rows into the caller's editor state —
 * it never saves directly, so a bad import is always reviewable before it touches the backend.
 */
export function ImportCsvDialog({ open, onClose, onImport }: Props) {
  const [fileName, setFileName] = useState('');
  const [result, setResult] = useState<CsvImportResult | null>(null);
  const [parseError, setParseError] = useState('');

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setParseError('');
    setResult(null);
    setFileName(file.name);
    const reader = new FileReader();
    reader.onload = (ev) => {
      const text = ev.target?.result as string;
      const parsed = parseTabular(text);
      if (!parsed) setParseError('無法解析檔案 — 請確認是含標題列的有效 CSV 檔案。');
      else setResult(mapCsvRows(parsed.rows));
    };
    reader.readAsText(file, 'UTF-8');
  };

  const handleClose = () => {
    setFileName('');
    setResult(null);
    setParseError('');
    onClose();
  };

  const handleImport = () => {
    if (!result || result.valid.length === 0) return;
    onImport(result.valid);
    handleClose();
  };

  const previewHeaders = ['類型', '代號', '名稱', '市場', '計價幣別', '成本幣別', '數量', '成本'];
  const previewRows = (result?.invalid.slice(0, 5) ?? []).map((e) => ({ ...e, ok: false }));
  const previewValid = (result?.valid.slice(0, Math.max(0, 5 - previewRows.length)) ?? []).map(
    (p) => ({ position: p, ok: true }),
  );

  return (
    <Dialog open={open} onClose={handleClose} maxWidth="md" fullWidth>
      <DialogTitle sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        <UploadFileIcon fontSize="small" />
        匯入 CSV
      </DialogTitle>
      <DialogContent sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
        <Typography variant="body2" color="text.secondary">
          選擇一個依「下載匯入範本」欄位格式準備的 CSV
          檔案。匯入後仍需在下方各區塊確認內容，並按「儲存快照」才會實際生效。
        </Typography>

        <Box>
          <Button variant="outlined" component="label" startIcon={<UploadFileIcon />}>
            選擇 CSV 檔案
            <input hidden type="file" accept=".csv" onChange={handleFileChange} />
          </Button>
          {fileName && (
            <Typography variant="caption" color="text.secondary" sx={{ ml: 1.5 }}>
              {fileName}
            </Typography>
          )}
        </Box>

        {parseError && <Alert severity="error">{parseError}</Alert>}

        {result && (
          <Box>
            <Typography variant="body2" sx={{ mb: 1 }}>
              共 {result.valid.length + result.invalid.length} 筆，可匯入 {result.valid.length} 筆
              {result.invalid.length > 0 && `，${result.invalid.length} 筆略過（欄位有誤）`}
            </Typography>
            {result.invalid.length > 0 && (
              <Alert severity="warning" sx={{ mb: 1 }}>
                有欄位錯誤的資料列不會被加入，可修正 CSV 後重新選擇檔案。
              </Alert>
            )}
            {(previewRows.length > 0 || previewValid.length > 0) && (
              <TableContainer component={Paper} variant="outlined" sx={{ maxHeight: 240 }}>
                <Table size="small" stickyHeader>
                  <TableHead>
                    <TableRow>
                      <TableCell sx={{ fontWeight: 600 }}>狀態</TableCell>
                      {previewHeaders.map((h) => (
                        <TableCell key={h} sx={{ fontWeight: 600, whiteSpace: 'nowrap' }}>
                          {h}
                        </TableCell>
                      ))}
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {previewRows.map((e, i) => (
                      <TableRow key={`invalid-${i}`}>
                        <TableCell sx={{ color: 'error.main', whiteSpace: 'nowrap' }}>
                          {e.error}
                        </TableCell>
                        {previewHeaders.map((h) => (
                          <TableCell key={h}>{e.row[h] ?? ''}</TableCell>
                        ))}
                      </TableRow>
                    ))}
                    {previewValid.map((e, i) => (
                      <TableRow key={`valid-${i}`}>
                        <TableCell sx={{ color: 'success.main' }}>可匯入</TableCell>
                        <TableCell>{e.position.assetType}</TableCell>
                        <TableCell>{e.position.symbol}</TableCell>
                        <TableCell>{e.position.name}</TableCell>
                        <TableCell>{e.position.market}</TableCell>
                        <TableCell>{e.position.currency}</TableCell>
                        <TableCell>{e.position.costCurrency ?? ''}</TableCell>
                        <TableCell>{e.position.quantity}</TableCell>
                        <TableCell>{e.position.costBasis ?? ''}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
            )}
          </Box>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={handleClose}>取消</Button>
        <Button
          variant="contained"
          onClick={handleImport}
          disabled={!result || result.valid.length === 0}
        >
          加入 {result && result.valid.length > 0 ? `(${result.valid.length} 筆)` : ''}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
