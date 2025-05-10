import { Box, Button, CircularProgress, TextField, Typography } from '@mui/material';
import { useState } from 'react';

function PDFUnlocker() {
  const [pdfFile, setPdfFile] = useState<File | null>(null);
  const [password, setPassword] = useState<string>('');
  const [isLoading, setIsLoading] = useState(false);
  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files) {
      setPdfFile(e.target.files[0]);
    }
  };
  async function downloadPdf() {
    if (!pdfFile || !password) return;
    setIsLoading(true);
    try {
      const formData = new FormData();
      formData.append('pdfFile', pdfFile);
      formData.append('password', password);

      const response = await fetch('/api/PDF/unlock', {
        method: 'POST',
        body: formData,
      });

      if (!response.ok) {
        throw new Error('Failed to download the PDF file');
      }

      const blob = await response.blob();
      const url = window.URL.createObjectURL(blob);

      const link = document.createElement('a');
      link.href = url;
      link.download = 'unlocked.pdf';
      document.body.appendChild(link);
      link.click();
      link.remove();

      window.URL.revokeObjectURL(url);
    } catch (error) {
      console.error('Error downloading the PDF:', error);
    } finally {
      setIsLoading(false);
    }
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();

    if (!pdfFile || !password) {
      alert('Please upload a PDF file and enter the password.');
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
        <Box mb={2}>
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
        <Box mb={2}>
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
        <Button variant="contained" color="primary" type="submit" disabled={isLoading} fullWidth>
          {isLoading ? <CircularProgress size={24} color="inherit" /> : 'Unlock PDF & Compress PDF'}
        </Button>
      </form>
    </Box>
  );
}

export default PDFUnlocker;
