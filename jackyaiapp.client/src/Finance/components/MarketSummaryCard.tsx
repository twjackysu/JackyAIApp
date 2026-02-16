import Avatar from '@mui/material/Avatar';
import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import CardHeader from '@mui/material/CardHeader';
import Chip from '@mui/material/Chip';
import { grey } from '@mui/material/colors';
import Divider from '@mui/material/Divider';
import Typography from '@mui/material/Typography';

import { StrategicInsight } from '@/apis/financeApis/types';

import {
  getImplicationColor,
  getImplicationChipColor,
  getImplicationLabel,
} from '../utils/financeHelpers';
import TrendIcon from './TrendIcon';

interface MarketSummaryCardProps {
  stock: StrategicInsight;
}

/**
 * Card displaying market summary for a single stock
 */
export const MarketSummaryCard = ({ stock }: MarketSummaryCardProps) => {
  return (
    <Card
      elevation={3}
      sx={{
        height: '100%',
        display: 'flex',
        flexDirection: 'column',
        transition: 'transform 0.2s',
        '&:hover': {
          transform: 'translateY(-4px)',
        },
      }}
    >
      <CardHeader
        avatar={
          <Avatar
            sx={{
              bgcolor: getImplicationColor(stock.implication),
              width: 56,
              height: 56,
            }}
          >
            {stock.stockCode}
          </Avatar>
        }
        title={
          <Typography variant="h6" component="div">
            {stock.companyName}
          </Typography>
        }
        subheader={`股票代碼: ${stock.stockCode}`}
        action={
          <Chip
            icon={<TrendIcon trend={stock.implication} />}
            label={getImplicationLabel(stock.implication)}
            color={getImplicationChipColor(stock.implication)}
            variant="filled"
            sx={{ fontWeight: 'bold', mt: 1 }}
          />
        }
      />

      <CardContent sx={{ flexGrow: 1 }}>
        <Typography variant="h6" gutterBottom sx={{ fontWeight: 'bold' }}>
          {stock.title}
        </Typography>

        <Typography variant="body2" color="text.secondary" paragraph>
          {stock.summary}
        </Typography>

        {stock.suggestedAction && (
          <>
            <Divider sx={{ my: 1.5 }} />
            <Box sx={{ mt: 2 }}>
              <Typography variant="subtitle2" fontWeight="bold" color="primary">
                投資建議:
              </Typography>
              <Typography variant="body2">{stock.suggestedAction}</Typography>
            </Box>
          </>
        )}
      </CardContent>

      <Box
        sx={{
          bgcolor: grey[900],
          p: 1,
          borderTop: 1,
          borderColor: 'divider',
          display: 'flex',
          justifyContent: 'flex-end',
        }}
      >
        <Typography variant="caption" color="text.secondary">
          發布日期: {stock.date}
        </Typography>
      </Box>
    </Card>
  );
};

export default MarketSummaryCard;
