import type {
  DateGranularity,
  FilterDimensionDto,
  GroupFieldDto,
} from '../../apis/dataAnalysisApis/types';
import type { ActiveFilter, FieldTransformDef, ViewByEntry } from '../types';

export function makeViewByEntry(fieldName = ''): ViewByEntry {
  return {
    id: Date.now() + Math.random(),
    fieldName,
    type: 'Plain',
    alias: '',
    transformId: null,
  };
}

/**
 * Convert a ViewByEntry to the DTO the server expects.
 * When type === 'Transform', resolves the referenced FieldTransformDef.
 * Falls back to Plain if the transform is missing or the entry is misconfigured.
 */
export function toGroupFieldDto(e: ViewByEntry, transforms: FieldTransformDef[]): GroupFieldDto {
  if (e.type === 'Transform' && e.transformId !== null) {
    const t = transforms.find((t) => t.id === e.transformId);
    if (t) {
      return {
        type: t.type,
        fieldName: e.fieldName,
        alias: e.alias || null,
        granularity: t.type === 'DateTrunc' ? t.granularity : null,
        fallback:
          t.type === 'NumericBin' || t.type === 'StringGrouping' ? t.fallback || 'Other' : null,
        bins:
          t.type === 'NumericBin'
            ? t.bins.map((b) => ({
                min: b.min === '' ? null : parseFloat(b.min),
                max: b.max === '' ? null : parseFloat(b.max),
                label: b.label,
              }))
            : null,
        groups:
          t.type === 'StringGrouping'
            ? t.groups.map((g) => ({ label: g.label, values: g.values }))
            : null,
      };
    }
  }

  // Plain (also the fallback when a Transform reference is broken)
  return {
    type: 'Plain',
    fieldName: e.fieldName,
    alias: e.alias || null,
    granularity: null,
    fallback: null,
    bins: null,
    groups: null,
  };
}

// ── Period label → date range helper ─────────────────────────────────────────

/**
 * Converts a DateTrunc output label (e.g. "2024-03") to an inclusive [start, end]
 * date-string pair suitable for a `Between` filter.
 * Returns null when the label cannot be parsed.
 *
 * Label formats produced by the MsSql visitor:
 *   Year    → "2024"
 *   Quarter → "2024Q1"
 *   Month   → "2024-03"
 *   Week    → "2024-W10"
 *   Day     → "2024-03-15"
 *   Hour    → "2024-03-15 14:00"
 */
function periodToDateRange(label: string, granularity: DateGranularity): [string, string] | null {
  try {
    switch (granularity) {
      case 'Year': {
        const y = parseInt(label, 10);
        if (isNaN(y)) return null;
        return [`${y}-01-01`, `${y}-12-31`];
      }
      case 'Quarter': {
        const m = /^(\d{4})Q(\d)$/.exec(label);
        if (!m) return null;
        const y = parseInt(m[1], 10);
        const q = parseInt(m[2], 10);
        const startMonth = (q - 1) * 3 + 1;
        const endMonth = q * 3;
        const lastDay = new Date(y, endMonth, 0).getDate();
        return [
          `${y}-${String(startMonth).padStart(2, '0')}-01`,
          `${y}-${String(endMonth).padStart(2, '0')}-${String(lastDay).padStart(2, '0')}`,
        ];
      }
      case 'Month': {
        const m = /^(\d{4})-(\d{2})$/.exec(label);
        if (!m) return null;
        const y = parseInt(m[1], 10);
        const mo = parseInt(m[2], 10);
        const lastDay = new Date(y, mo, 0).getDate();
        return [`${label}-01`, `${label}-${String(lastDay).padStart(2, '0')}`];
      }
      case 'Week': {
        // ISO week: find Monday of week N of year Y
        const m = /^(\d{4})-W(\d{2})$/.exec(label);
        if (!m) return null;
        const y = parseInt(m[1], 10);
        const w = parseInt(m[2], 10);
        const jan4 = new Date(y, 0, 4);
        const dow = jan4.getDay() || 7; // Mon=1 … Sun=7
        const week1Mon = new Date(jan4.getTime() - (dow - 1) * 86_400_000);
        const weekStart = new Date(week1Mon.getTime() + (w - 1) * 7 * 86_400_000);
        const weekEnd = new Date(weekStart.getTime() + 6 * 86_400_000);
        return [weekStart.toISOString().slice(0, 10), weekEnd.toISOString().slice(0, 10)];
      }
      case 'Day':
        return [label, label]; // "2024-03-15" → same start and end
      case 'Hour': {
        const m = /^(\d{4}-\d{2}-\d{2}) (\d{2}):00$/.exec(label);
        if (!m) return null;
        return [`${m[1]} ${m[2]}:00:00`, `${m[1]} ${m[2]}:59:59`];
      }
      default:
        return null;
    }
  } catch {
    return null;
  }
}

/**
 * Convert ActiveFilters to server DTOs.
 *
 * - StringGrouping: expand selected group labels → constituent raw values → IN filter.
 * - NumericBin:     merge selected bin ranges → single Between / GreaterThan / LessThan.
 * - DateTrunc:      convert selected period labels → merged date range → Between filter.
 * - Plain:          pass raw selected values as-is (IN filter).
 */
export function toFilterDtos(
  filters: ActiveFilter[],
  transforms: FieldTransformDef[],
): FilterDimensionDto[] {
  return filters
    .filter((f) => f.fieldName && f.values.length > 0)
    .flatMap((f): FilterDimensionDto[] => {
      if (f.useTransform && f.transformId !== null) {
        const t = transforms.find((t) => t.id === f.transformId);

        // ── StringGrouping: labels → raw IN-list ───────────────────────────
        if (t?.type === 'StringGrouping') {
          const rawValues = f.values.flatMap((label) => {
            const grp = t.groups.find((g) => g.label === label);
            return grp ? grp.values : [label];
          });
          if (rawValues.length > 0) return [{ fieldName: f.fieldName, values: rawValues }];
        }

        // ── NumericBin: merge selected bins into a single range ────────────
        if (t?.type === 'NumericBin') {
          const selected = t.bins.filter((b) => f.values.includes(b.label));
          if (selected.length > 0) {
            const hasOpenMin = selected.some((b) => b.min === '');
            const hasOpenMax = selected.some((b) => b.max === '');
            // All-open range = every row passes = skip filter entirely
            if (hasOpenMin && hasOpenMax) return [];

            const mins = selected.filter((b) => b.min !== '').map((b) => parseFloat(b.min));
            const maxes = selected.filter((b) => b.max !== '').map((b) => parseFloat(b.max));
            const lo = mins.length > 0 ? Math.min(...mins) : null;
            const hi = maxes.length > 0 ? Math.max(...maxes) : null;

            if (hasOpenMin && hi !== null)
              return [{ fieldName: f.fieldName, values: [String(hi)], op: 'LessThan' }];
            if (hasOpenMax && lo !== null)
              return [{ fieldName: f.fieldName, values: [String(lo)], op: 'GreaterThan' }];
            if (lo !== null && hi !== null)
              return [{ fieldName: f.fieldName, values: [String(lo), String(hi)], op: 'Between' }];
          }
        }

        // ── DateTrunc: period labels → merged date range ───────────────────
        if (t?.type === 'DateTrunc') {
          const ranges = f.values
            .map((l) => periodToDateRange(l, t.granularity))
            .filter((r): r is [string, string] => r !== null);
          if (ranges.length > 0) {
            const starts = ranges.map((r) => r[0]).sort();
            const ends = ranges.map((r) => r[1]).sort();
            return [
              {
                fieldName: f.fieldName,
                values: [starts[0], ends[ends.length - 1]],
                op: 'Between',
              },
            ];
          }
        }
      }

      // Plain (or transform expansion failed / not applicable)
      return [{ fieldName: f.fieldName, values: f.values }];
    });
}
