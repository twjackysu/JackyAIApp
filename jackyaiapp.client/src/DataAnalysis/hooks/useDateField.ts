import { useEffect, useState } from 'react';

import { DATE_CANONICAL_TYPES } from '../constants';

import type { FieldDefinition } from '../../apis/dataAnalysisApis/types';

export function useDateField(fields: FieldDefinition[]) {
  const [dateField, setDateField] = useState('OrderDate');

  // Auto-detect: switch to the first date-typed field when schema changes and
  // the current value no longer exists in the new schema.
  useEffect(() => {
    if (fields.length === 0) return;
    const exists = fields.some((f) => f.name === dateField);
    if (!exists) {
      const firstDate = fields.find((f) => DATE_CANONICAL_TYPES.includes(f.canonicalType))?.name;
      setDateField(firstDate ?? fields[0].name);
    }
  }, [fields]); // eslint-disable-line react-hooks/exhaustive-deps

  return { dateField, setDateField };
}
