import DeleteOutlineIcon from '@mui/icons-material/DeleteOutlineOutlined';
import HistoryIcon from '@mui/icons-material/History';
import RestoreIcon from '@mui/icons-material/Restore';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import IconButton from '@mui/material/IconButton';
import { useTheme } from '@mui/material/styles';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import useMediaQuery from '@mui/material/useMediaQuery';
import { useState } from 'react';

import {
  useDeleteSnapshotMutation,
  useGetUpdateHistoryQuery,
  useLazyGetSnapshotHoldingsQuery,
} from '@/apis/portfolioApis';
import { PositionInput } from '@/apis/portfolioApis/types';

interface Props {
  onLoadSnapshot: (positions: PositionInput[]) => void;
}

export function UpdateHistoryDialog({ onLoadSnapshot }: Props) {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const [open, setOpen] = useState(false);
  const { data: res, isLoading } = useGetUpdateHistoryQuery(undefined, { skip: !open });
  const [fetchSnapshot, { isFetching: isLoadingSnap }] = useLazyGetSnapshotHoldingsQuery();
  const [deleteSnapshot] = useDeleteSnapshotMutation();
  const [busyId, setBusyId] = useState<number | null>(null);

  const history = res?.data ?? [];

  const handleLoad = async (snapshotId: number) => {
    setBusyId(snapshotId);
    try {
      const r = await fetchSnapshot(snapshotId).unwrap();
      if (r?.data?.positions) {
        onLoadSnapshot(r.data.positions);
        setOpen(false);
      }
    } finally {
      setBusyId(null);
    }
  };

  const handleDelete = async (snapshotId: number) => {
    if (!window.confirm('刪除這筆持倉紀錄？走勢圖會跟著重算。')) return;
    setBusyId(snapshotId);
    try {
      await deleteSnapshot(snapshotId).unwrap();
    } finally {
      setBusyId(null);
    }
  };

  return (
    <>
      <Button variant="outlined" startIcon={<HistoryIcon />} onClick={() => setOpen(true)}>
        更新紀錄
      </Button>

      <Dialog
        open={open}
        onClose={() => setOpen(false)}
        maxWidth="sm"
        fullWidth
        fullScreen={fullScreen}
      >
        <DialogTitle>持倉更新紀錄</DialogTitle>
        <DialogContent>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            每次更新持倉會留一筆快照。「載入」會把該日持倉填進編輯器，存檔後即建立今天的新快照。
          </Typography>

          {isLoading ? (
            <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}>
              <CircularProgress size={28} />
            </Box>
          ) : history.length === 0 ? (
            <Typography color="text.secondary" sx={{ py: 3, textAlign: 'center' }}>
              尚無紀錄
            </Typography>
          ) : (
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>生效日</TableCell>
                  <TableCell align="right" sx={{ display: { xs: 'none', sm: 'table-cell' } }}>
                    項目數
                  </TableCell>
                  <TableCell align="right" sx={{ display: { xs: 'none', sm: 'table-cell' } }}>
                    匯率
                  </TableCell>
                  <TableCell align="right">操作</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {history.map((h) => (
                  <TableRow key={h.snapshotId} hover>
                    <TableCell>{h.effectiveDate}</TableCell>
                    <TableCell align="right" sx={{ display: { xs: 'none', sm: 'table-cell' } }}>
                      {h.positionCount}
                    </TableCell>
                    <TableCell align="right" sx={{ display: { xs: 'none', sm: 'table-cell' } }}>
                      {h.usdTwdRate.toFixed(2)}
                    </TableCell>
                    <TableCell align="right">
                      {/* Real touch targets, spaced apart — cramped size="small" buttons
                          sitting right next to each other were hard to tap accurately on mobile. */}
                      <Box sx={{ display: 'flex', justifyContent: 'flex-end', gap: 0.5 }}>
                        <Tooltip title="載入到編輯器">
                          <span>
                            <IconButton
                              disabled={busyId === h.snapshotId && isLoadingSnap}
                              onClick={() => handleLoad(h.snapshotId)}
                            >
                              <RestoreIcon fontSize="small" />
                            </IconButton>
                          </span>
                        </Tooltip>
                        <Tooltip title="刪除這筆快照">
                          <span>
                            <IconButton
                              disabled={busyId === h.snapshotId}
                              onClick={() => handleDelete(h.snapshotId)}
                            >
                              <DeleteOutlineIcon fontSize="small" />
                            </IconButton>
                          </span>
                        </Tooltip>
                      </Box>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </DialogContent>
      </Dialog>
    </>
  );
}
