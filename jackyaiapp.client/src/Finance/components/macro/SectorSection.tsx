import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';

import { ChangeText, DirectionIcon } from './atoms';
import { formatNumber } from './format';

import type { SectorIndex } from '@/apis/financeApis/types';

const sectorDisplayNames: Record<string, string> = {
  發行量加權股價指數: '加權指數',
  半導體類指數: '半導體',
  電子工業類指數: '電子工業',
  金融保險類指數: '金融保險',
  航運類指數: '航運',
  臺灣50指數: '台灣50',
  生技醫療類指數: '生技醫療',
  通信網路類指數: '通信網路',
};

export const SectorSection = ({ sectors }: { sectors: SectorIndex[] }) => {
  if (sectors.length === 0) return null;

  // Exclude 加權指數 since it's already shown in the main card
  const filtered = sectors.filter((s) => s.name !== '發行量加權股價指數');

  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Typography variant="subtitle2" color="text.secondary" gutterBottom>
          🏭 產業類指數
        </Typography>
        <TableContainer>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>產業</TableCell>
                <TableCell align="right">指數</TableCell>
                <TableCell align="right">漲跌</TableCell>
                <TableCell align="right">幅度</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {filtered.map((s) => (
                <TableRow key={s.name} hover>
                  <TableCell>
                    <Stack
                      direction="row"
                      spacing={0.5}
                      sx={{
                        alignItems: 'center',
                      }}
                    >
                      <DirectionIcon direction={s.direction} />
                      <Typography variant="body2">
                        {sectorDisplayNames[s.name] ?? s.name}
                      </Typography>
                    </Stack>
                  </TableCell>
                  <TableCell align="right">
                    <Typography variant="body2">
                      {formatNumber(Math.round(s.closeIndex))}
                    </Typography>
                  </TableCell>
                  <TableCell align="right">
                    <ChangeText value={s.direction === '-' ? -s.changePoints : s.changePoints} />
                  </TableCell>
                  <TableCell align="right">
                    <ChangeText value={s.changePercent} suffix="%" />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      </CardContent>
    </Card>
  );
};
