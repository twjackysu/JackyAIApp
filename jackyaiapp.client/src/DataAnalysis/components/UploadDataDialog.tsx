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
import { useRef, useState } from 'react';

import { useUploadSessionMutation } from '../../apis/dataAnalysisApis';

import type { SessionInfo } from '../../apis/dataAnalysisApis/types';

interface UploadDataDialogProps {
  open: boolean;
  onClose: () => void;
  onSessionCreated: (info: SessionInfo) => void;
}

// ── CSV / TSV parsing ─────────────────────────────────────────────────────────

function detectSep(text: string): string {
  const firstLine = text.split('\n')[0] ?? '';
  const tabs = (firstLine.match(/\t/g) ?? []).length;
  const commas = (firstLine.match(/,/g) ?? []).length;
  return tabs >= commas ? '\t' : ',';
}

function parseLine(line: string, sep: string): string[] {
  if (sep !== ',') return line.split(sep).map((c) => c.trim());

  // Quoted CSV parsing
  const result: string[] = [];
  let current = '';
  let inQuotes = false;
  for (let i = 0; i < line.length; i++) {
    const ch = line[i];
    if (ch === '"') {
      if (inQuotes && line[i + 1] === '"') {
        current += '"';
        i++;
      } else inQuotes = !inQuotes;
    } else if (ch === ',' && !inQuotes) {
      result.push(current.trim());
      current = '';
    } else {
      current += ch;
    }
  }
  result.push(current.trim());
  return result;
}

function parseTabular(text: string): { headers: string[]; rows: Record<string, string>[] } | null {
  const lines = text
    .trim()
    .split('\n')
    .filter((l) => l.trim().length > 0);
  if (lines.length < 2) return null;

  const sep = detectSep(text);
  const headers = parseLine(lines[0], sep).map((h) => h.replace(/^"|"$/g, '').trim());
  if (headers.length < 2) return null;

  const rows = lines.slice(1).map((line) => {
    const cells = parseLine(line, sep);
    return Object.fromEntries(
      headers.map((h, i) => [h, (cells[i] ?? '').replace(/^"|"$/g, '').trim()]),
    );
  });

  return { headers, rows };
}

// ── Component ─────────────────────────────────────────────────────────────────

export function UploadDataDialog({ open, onClose, onSessionCreated }: UploadDataDialogProps) {
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
      setErr('Could not detect columns — make sure the first row contains headers.');
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
      if (!result) setErr('Could not parse file — ensure it is a valid CSV/TSV with headers.');
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
      setErr('Upload failed — please try again.');
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
    <Dialog open={open} onClose={handleClose} maxWidth="md" fullWidth>
      <DialogTitle sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        <CloudUploadIcon fontSize="small" />
        Upload Data
        <Chip
          icon={<AccessTimeIcon />}
          label="Temporary — 2 hours"
          color="warning"
          size="small"
          sx={{ ml: 1 }}
        />
      </DialogTitle>
      <DialogContent>
        <Alert severity="warning" sx={{ mb: 2 }}>
          <strong>Temporary session only.</strong> This data is stored in server memory and will be
          automatically deleted after 2 hours. It is not saved to any database.
        </Alert>

        <TextField
          label="Dataset name (optional)"
          size="small"
          fullWidth
          value={datasetName}
          onChange={(e) => setName(e.target.value)}
          placeholder="e.g. Asset Trend 2025"
          sx={{ mb: 2 }}
        />

        <Tabs value={tab} onChange={(_, v: number) => setTab(v)} sx={{ mb: 2 }}>
          <Tab icon={<ContentPasteIcon />} iconPosition="start" label="Paste from Excel" />
          <Tab icon={<CloudUploadIcon />} iconPosition="start" label="Upload CSV / TSV" />
        </Tabs>

        {tab === 0 && (
          <TextField
            multiline
            rows={8}
            fullWidth
            size="small"
            placeholder={
              'Paste your Excel data here (copy cells including the header row):\n\n' +
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
              Choose CSV / TSV file
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
              Detected <strong>{parsed.rows.length.toLocaleString()}</strong> rows ×{' '}
              <strong>{parsed.headers.length}</strong> columns — preview (first 5 rows):
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
          Cancel
        </Button>
        <Button
          variant="contained"
          onClick={handleSubmit}
          disabled={!parsed || parsed.rows.length === 0 || isLoading}
          startIcon={isLoading ? <CircularProgress size={16} /> : <CloudUploadIcon />}
        >
          {isLoading ? 'Uploading…' : 'Load into session'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
