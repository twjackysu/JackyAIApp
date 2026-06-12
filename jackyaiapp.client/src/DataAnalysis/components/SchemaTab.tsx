import CalendarTodayIcon from '@mui/icons-material/CalendarToday';
import Alert from '@mui/material/Alert';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Paper from '@mui/material/Paper';
import Select from '@mui/material/Select';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';

import { DATE_CANONICAL_TYPES } from '../constants';
import { canonicalTypeColor } from '../utils/formatters';

import type { FieldDefinition } from '../../apis/dataAnalysisApis/types';

interface SchemaTabProps {
  fields: FieldDefinition[];
  isLoading: boolean;
  error: unknown;
  dateField: string;
  onDateFieldChange: (field: string) => void;
}

export function SchemaTab({
  fields,
  isLoading,
  error,
  dateField,
  onDateFieldChange,
}: SchemaTabProps) {
  const dateFields = fields.filter((f) => DATE_CANONICAL_TYPES.includes(f.canonicalType));
  const dateFieldList = dateFields.length > 0 ? dateFields : fields;

  return (
    <>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        Columns detected in this data, with their types.
      </Typography>

      {isLoading && <CircularProgress size={24} />}
      {error && <Alert severity="error">Failed to load schema.</Alert>}

      {fields.length > 0 && (
        <>
          <Paper
            variant="outlined"
            sx={{ p: 2, mb: 2, display: 'flex', alignItems: 'center', gap: 2, flexWrap: 'wrap' }}
          >
            <CalendarTodayIcon fontSize="small" color="action" />
            <FormControl size="small" sx={{ minWidth: 220 }}>
              <InputLabel>Date Field</InputLabel>
              <Select
                value={fields.some((f) => f.name === dateField) ? dateField : ''}
                label="Date Field"
                onChange={(e) => onDateFieldChange(e.target.value as string)}
              >
                {dateFieldList.map((f) => (
                  <MenuItem key={f.name} value={f.name}>
                    {f.name}
                  </MenuItem>
                ))}
              </Select>
            </FormControl>
            <Typography variant="caption" color="text.secondary">
              Auto-detected from schema — drives the <strong>Latest date</strong> stat above and the
              default sort order in <strong>Details</strong>.
            </Typography>
          </Paper>

          <TableContainer component={Paper} variant="outlined">
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell>Column</TableCell>
                  <TableCell>Native Type</TableCell>
                  <TableCell>Canonical Type</TableCell>
                  <TableCell>Nullable</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {fields.map((f) => (
                  <TableRow key={f.name} hover>
                    <TableCell sx={{ fontWeight: 500 }}>{f.name}</TableCell>
                    <TableCell>
                      <code>{f.dataType}</code>
                    </TableCell>
                    <TableCell>
                      <Chip
                        label={f.canonicalType}
                        size="small"
                        color={canonicalTypeColor(f.canonicalType)}
                      />
                    </TableCell>
                    <TableCell>{f.isNullable ? 'Yes' : 'No'}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        </>
      )}
    </>
  );
}
