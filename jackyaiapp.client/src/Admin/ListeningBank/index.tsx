import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Pagination from '@mui/material/Pagination';
import Paper from '@mui/material/Paper';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

import { useGetUserInfoQuery } from '@/apis/accountApis';
import { useGenerateListeningQuestionMutation, useGetListeningBankQuery } from '@/apis/examApis';
import CenteredSpinner from '@/components/CenteredSpinner';
import FetchBaseQueryErrorMessage from '@/components/FetchBaseQueryErrorMessage';

const PAGE_SIZE = 20;

function ListeningBankAdmin() {
  const { data: userInfoRes } = useGetUserInfoQuery();
  const isAdmin = !!userInfoRes?.data.isAdmin;

  const [page, setPage] = useState(1);
  const { data, isFetching, isError, error } = useGetListeningBankQuery(
    { pageNumber: page, pageSize: PAGE_SIZE },
    { skip: !isAdmin },
  );
  const [generate, { isLoading: isGenerating }] = useGenerateListeningQuestionMutation();

  if (!userInfoRes) {
    return <CenteredSpinner sx={{ p: 4 }} />;
  }
  if (!isAdmin) {
    return (
      <Alert severity="error" sx={{ maxWidth: 600, mx: 'auto', mt: 4 }}>
        Admin access required.
      </Alert>
    );
  }

  const items = data?.data.items ?? [];
  const totalCount = data?.data.totalCount ?? 0;
  const pageCount = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

  return (
    <Box sx={{ maxWidth: 1000, mx: 'auto', p: 2 }}>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
        <Typography variant="h5">聽力題庫 (Listening Question Bank)</Typography>
        <Button variant="contained" onClick={() => generate()} disabled={isGenerating}>
          {isGenerating ? '生成中…' : 'AI 產生新題目'}
        </Button>
      </Box>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        共 {totalCount} 題
      </Typography>
      {isError && <FetchBaseQueryErrorMessage error={error} />}
      {isFetching ? (
        <CenteredSpinner sx={{ p: 4 }} />
      ) : (
        <TableContainer component={Paper}>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>圖片</TableCell>
                <TableCell>Id</TableCell>
                <TableCell>選項</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {items.map((item) => (
                <TableRow key={item.id}>
                  <TableCell sx={{ verticalAlign: 'top' }}>
                    <Box
                      component="img"
                      src={item.imageUrl}
                      alt={item.id}
                      sx={{ width: 96, height: 72, objectFit: 'cover', borderRadius: 1 }}
                    />
                  </TableCell>
                  <TableCell sx={{ verticalAlign: 'top' }}>{item.id}</TableCell>
                  <TableCell>
                    {item.options.map((option, i) => (
                      <Box key={option.id} sx={{ mb: 0.5 }}>
                        <Typography
                          variant="body2"
                          sx={{
                            fontWeight: option.isCorrect ? 700 : 400,
                            color: option.isCorrect ? 'success.main' : 'text.primary',
                          }}
                        >
                          {String.fromCharCode(65 + i)}. {option.text}
                          {option.isCorrect && ' ✓'}
                        </Typography>
                        <Typography variant="caption" color="text.secondary">
                          {option.explanation}
                        </Typography>
                      </Box>
                    ))}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      )}
      {pageCount > 1 && (
        <Box sx={{ display: 'flex', justifyContent: 'center', mt: 2 }}>
          <Pagination
            count={pageCount}
            page={page}
            onChange={(_, value) => setPage(value)}
            color="primary"
          />
        </Box>
      )}
    </Box>
  );
}

export default ListeningBankAdmin;
