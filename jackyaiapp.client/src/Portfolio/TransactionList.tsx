import DeleteIcon from '@mui/icons-material/Delete';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import IconButton from '@mui/material/IconButton';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';

import { useDeleteTransactionMutation, useGetTransactionsQuery } from '@/apis/portfolioApis';
import { TransactionDto } from '@/apis/portfolioApis/types';

const TYPE_LABELS: Record<string, { label: string; color: 'success' | 'error' | 'info' }> = {
  BUY: { label: '買入', color: 'success' },
  SELL: { label: '賣出', color: 'error' },
  DIVIDEND: { label: '配息', color: 'info' },
};

export function TransactionList() {
  const { data: res, isLoading } = useGetTransactionsQuery();
  const [deleteTransaction, { isLoading: isDeleting }] = useDeleteTransactionMutation();
  const transactions = res?.data ?? [];

  if (isLoading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', py: 6 }}>
        <CircularProgress />
      </Box>
    );
  }

  if (transactions.length === 0) {
    return (
      <Typography color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>
        尚無交易紀錄
      </Typography>
    );
  }

  const handleDelete = (tx: TransactionDto) => {
    if (window.confirm(`確定刪除 ${tx.symbol} ${tx.type} × ${tx.quantity} 嗎？`)) {
      deleteTransaction(tx.id);
    }
  };

  return (
    <TableContainer>
      <Table size="small">
        <TableHead>
          <TableRow>
            <TableCell>日期</TableCell>
            <TableCell>股票</TableCell>
            <TableCell>類型</TableCell>
            <TableCell align="right">股數</TableCell>
            <TableCell align="right">成交價</TableCell>
            <TableCell align="right">手續費</TableCell>
            <TableCell>備註</TableCell>
            <TableCell />
          </TableRow>
        </TableHead>
        <TableBody>
          {transactions.map((tx) => {
            const typeInfo = TYPE_LABELS[tx.type] ?? { label: tx.type, color: 'info' as const };
            return (
              <TableRow key={tx.id} hover>
                <TableCell>
                  <Typography variant="body2">{tx.date.slice(0, 10)}</Typography>
                </TableCell>
                <TableCell>
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
                    <Typography variant="body2" sx={{ fontWeight: 600 }}>
                      {tx.symbol}
                    </Typography>
                    <Chip
                      label={tx.market}
                      size="small"
                      sx={{ fontSize: 10, height: 16 }}
                      variant="outlined"
                      color={tx.market === 'TW' ? 'primary' : 'warning'}
                    />
                  </Box>
                  <Typography variant="caption" color="text.secondary">
                    {tx.companyName}
                  </Typography>
                </TableCell>
                <TableCell>
                  <Chip
                    label={typeInfo.label}
                    color={typeInfo.color}
                    size="small"
                    variant="outlined"
                  />
                </TableCell>
                <TableCell align="right">{tx.quantity.toLocaleString()}</TableCell>
                <TableCell align="right">
                  {tx.currency} {tx.price.toLocaleString('zh-TW', { minimumFractionDigits: 2 })}
                </TableCell>
                <TableCell align="right">{tx.fee > 0 ? `${tx.currency} ${tx.fee}` : '—'}</TableCell>
                <TableCell>
                  <Typography variant="caption" color="text.secondary">
                    {tx.note ?? ''}
                  </Typography>
                </TableCell>
                <TableCell align="right">
                  <Tooltip title="刪除">
                    <span>
                      <IconButton
                        size="small"
                        color="error"
                        onClick={() => handleDelete(tx)}
                        disabled={isDeleting}
                      >
                        <DeleteIcon fontSize="small" />
                      </IconButton>
                    </span>
                  </Tooltip>
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
