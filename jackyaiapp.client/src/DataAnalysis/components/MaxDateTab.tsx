import CalendarTodayIcon from '@mui/icons-material/CalendarToday';
import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import CircularProgress from '@mui/material/CircularProgress';
import Typography from '@mui/material/Typography';

interface MaxDateTabProps {
  dateField: string;
  activeTableName: string;
  defaultTable: string;
  maxDate: string | null | undefined;
  isLoading: boolean;
}

export function MaxDateTab({
  dateField,
  activeTableName,
  defaultTable,
  maxDate,
  isLoading,
}: MaxDateTabProps) {
  return (
    <>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        <strong>MaxDateAsync</strong> — MAX of a date field from the target table.
      </Typography>

      {isLoading && <CircularProgress size={24} />}

      <Box sx={{ display: 'flex', justifyContent: 'center' }}>
        <Card variant="outlined" sx={{ minWidth: 320, textAlign: 'center' }}>
          <CardContent sx={{ py: 4 }}>
            <CalendarTodayIcon sx={{ fontSize: 56, color: 'primary.main', mb: 2 }} />
            <Typography variant="subtitle1" color="text.secondary" gutterBottom>
              MAX(<code>{dateField}</code>) in <code>{activeTableName || defaultTable}</code>
            </Typography>
            <Typography variant="h3" color="primary" sx={{ fontWeight: 700 }}>
              {maxDate
                ? new Date(maxDate).toLocaleDateString('en-US', {
                    year: 'numeric',
                    month: 'short',
                    day: 'numeric',
                  })
                : isLoading
                  ? '…'
                  : '—'}
            </Typography>
            {maxDate && (
              <Typography variant="caption" color="text.secondary" sx={{ mt: 1, display: 'block' }}>
                {maxDate}
              </Typography>
            )}
            <Typography variant="caption" color="text.secondary" sx={{ mt: 2, display: 'block' }}>
              To change the date field, go to the <strong>① Schema</strong> tab.
            </Typography>
          </CardContent>
        </Card>
      </Box>
    </>
  );
}
