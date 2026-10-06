import { Alert, Box, Button, CircularProgress, TextField, Typography } from '@mui/material';
import { useState } from 'react';

import { getApiErrorMessage } from '@/apis/utils/errorMessage';

/** Builds the `FetchBaseQueryError` shape from a failed raw `fetch` so the message comes from
 * the one shared extractor instead of a hand-rolled parse of the body. */
async function failureMessage(response: Response): Promise<string> {
  const text = await response.text();
  let data: unknown = text;
  try {
    data = JSON.parse(text);
  } catch {
    /* plain-text body: pass it through as is */
  }
  return getApiErrorMessage({ status: response.status, data });
}

function unlockedFileName(original: string): string {
  return `${original.replace(/\.pdf$/i, '')}-unlocked.pdf`;
}

function PDFUnlocker() {
  const [pdfFile, setPdfFile] = useState<File | null>(null);
  const [password, setPassword] = useState<string>('');
  const [isLoading, setIsLoading] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);
  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files) {
      setPdfFile(e.target.files[0]);
      setErrorMessage(null);
      setSuccess(false);
    }
  };
  async function downloadPdf() {
    if (!pdfFile || !password) return;
    setIsLoading(true);
    setErrorMessage(null);
    setSuccess(false);
    try {
      const formData = new FormData();
      formData.append('pdfFile', pdfFile);
      formData.append('password', password);

      const response = await fetch('/api/PDF/unlock', {
        method: 'POST',
        body: formData,
      });

      if (!response.ok) {
        setErrorMessage(await failureMessage(response));
        return;
      }

      const blob = await response.blob();
      const url = window.URL.createObjectURL(blob);

      const link = document.createElement('a');
      link.href = url;
      link.download = unlockedFileName(pdfFile.name);
      document.body.appendChild(link);
      link.click();
      link.remove();

      window.URL.revokeObjectURL(url);
      setSuccess(true);
    } catch {
      setErrorMessage(getApiErrorMessage({ status: 'FETCH_ERROR', error: 'network' }));
    } finally {
      setIsLoading(false);
    }
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    if (!pdfFile || !password) {
      setErrorMessage('Please upload a PDF file and enter the password.');
      return;
    }
    await downloadPdf();
  };

  return (
    <Box sx={{ maxWidth: 400, mx: 'auto', mt: 5, p: 3, borderRadius: 2, boxShadow: 3 }}>
      <Typography variant="h5" gutterBottom>
        Unlock PDF & Compress PDF
      </Typography>
      <form onSubmit={handleSubmit}>
        <Box
          sx={{
            mb: 2,
          }}
        >
          <input
            type="file"
            accept="application/pdf"
            onChange={handleFileChange}
            style={{ display: 'none' }}
            id="file-upload"
          />
          <label htmlFor="file-upload">
            <Button variant="contained" component="span" fullWidth>
              {pdfFile ? pdfFile.name : 'Upload PDF'}
            </Button>
          </label>
        </Box>
        <Box
          sx={{
            mb: 2,
          }}
        >
          <TextField
            fullWidth
            variant="outlined"
            label="Enter password"
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
          />
        </Box>
        {errorMessage && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {errorMessage}
          </Alert>
        )}
        {success && (
          <Alert severity="success" sx={{ mb: 2 }}>
            Done — your unlocked PDF has been downloaded.
          </Alert>
        )}
        <Button variant="contained" color="primary" type="submit" disabled={isLoading} fullWidth>
          {isLoading ? <CircularProgress size={24} color="inherit" /> : 'Unlock PDF & Compress PDF'}
        </Button>
      </form>
    </Box>
  );
}

export default PDFUnlocker;
