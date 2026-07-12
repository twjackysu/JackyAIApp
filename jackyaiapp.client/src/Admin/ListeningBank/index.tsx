import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
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
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', p: 4 }}>
        <CircularProgress />
      </Box>
    );
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
        <Box sx={{ display: 'flex', justifyContent: 'center', p: 4 }}>
          <CircularProgress />
        </Box>
      ) : (
        <TableContainer component={Paper}>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>圖片</TableCell>
                <TableCell>Id</TableCell>
                <TableCell>正確答案</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {items.map((item) => {
                const correct = item.options.find((o) => o.isCorrect);
                return (
                  <TableRow key={item.id}>
                    <TableCell>
                      <Box
                        component="img"
                        src={item.imageUrl}
                        alt={item.id}
                        sx={{ width: 96, height: 72, objectFit: 'cover', borderRadius: 1 }}
                      />
                    </TableCell>
                    <TableCell>{item.id}</TableCell>
                    <TableCell>{correct?.text}</TableCell>
                  </TableRow>
                );
              })}
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
