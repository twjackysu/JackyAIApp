import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import Accordion from '@mui/material/Accordion';
import AccordionDetails from '@mui/material/AccordionDetails';
import AccordionSummary from '@mui/material/AccordionSummary';
import Chip from '@mui/material/Chip';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';

import {
  getDirectionLabel,
  getDirectionChipColor,
  getCategoryLabel,
  getCategoryEmoji,
  getScoreColor,
  getIndicatorDisplayName,
} from '../../utils/financeHelpers';

import type { IndicatorResult, IndicatorCategory } from '@/apis/financeApis/types';

interface IndicatorTableProps {
  indicators: IndicatorResult[];
  category: IndicatorCategory;
}

export const IndicatorTable = ({ indicators, category }: IndicatorTableProps) => {
  const filtered = indicators.filter((i) => i.category === category);
  if (filtered.length === 0) return null;
  return (
    <Accordion defaultExpanded={category === 'Technical'}>
      <AccordionSummary expandIcon={<ExpandMoreIcon />}>
        <Typography
          variant="h6"
          sx={{
            fontWeight: 'bold',
          }}
        >
          {getCategoryEmoji(category)} {getCategoryLabel(category)}指標明細 ({filtered.length})
        </Typography>
      </AccordionSummary>
      <AccordionDetails>
        <TableContainer>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>指標</TableCell>
                <TableCell align="center">數值</TableCell>
                <TableCell align="center">方向</TableCell>
                <TableCell align="center">分數</TableCell>
                <TableCell>訊號</TableCell>
                <TableCell>說明</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {filtered.map((ind) => (
                <TableRow key={ind.name} hover>
                  <TableCell>
                    <Typography
                      variant="body2"
                      sx={{
                        fontWeight: 'bold',
                      }}
                    >
                      {getIndicatorDisplayName(ind.name)}
                    </Typography>
                  </TableCell>
                  <TableCell align="center">{ind.value.toFixed(2)}</TableCell>
                  <TableCell align="center">
                    <Chip
                      label={getDirectionLabel(ind.direction)}
                      color={getDirectionChipColor(ind.direction)}
                      size="small"
                      variant="outlined"
                    />
                  </TableCell>
                  <TableCell align="center">
                    <Typography
                      variant="body2"
                      sx={{
                        fontWeight: 'bold',
                        color: getScoreColor(ind.score),
                      }}
                    >
                      {ind.score}
                    </Typography>
                  </TableCell>
                  <TableCell>{ind.signal}</TableCell>
                  <TableCell>
                    <Typography variant="caption" color="text.secondary">
                      {ind.reason}
                    </Typography>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      </AccordionDetails>
    </Accordion>
  );
};
