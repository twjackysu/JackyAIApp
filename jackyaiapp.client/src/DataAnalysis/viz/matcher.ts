import type { DataShape, EligibilityResult, VizCapability, VizId } from './types';

const fmtRange = (min: number, max: number): string => {
  if (max === Number.POSITIVE_INFINITY) return `≥${min}`;
  if (min === max) return `${min}`;
  return `${min}–${max}`;
};

/** Decide whether a viz can render a given data shape, with a reason if not. */
export function evaluate(cap: VizCapability, shape: DataShape): EligibilityResult {
  if (shape.groupCount < cap.minGroups || shape.groupCount > cap.maxGroups) {
    return {
      ok: false,
      reason: `需要 ${fmtRange(cap.minGroups, cap.maxGroups)} 個分組維度（目前 ${shape.groupCount}）`,
    };
  }
  if (shape.measureCount < cap.minMeasures || shape.measureCount > cap.maxMeasures) {
    return {
      ok: false,
      reason: `需要 ${fmtRange(cap.minMeasures, cap.maxMeasures)} 個數值欄（目前 ${shape.measureCount}）`,
    };
  }
  if (cap.requiresTemporal && !shape.temporalKey) {
    return { ok: false, reason: '需要一個時間維度（日期 / 月 / 季 / 年…）' };
  }
  if (cap.minTemporalPoints && (shape.temporalPoints ?? 0) < cap.minTemporalPoints) {
    return {
      ok: false,
      reason: `需要至少 ${cap.minTemporalPoints} 個時間點（目前 ${shape.temporalPoints ?? 0}）`,
    };
  }
  if (cap.customEligible) return cap.customEligible(shape);
  return { ok: true };
}

/**
 * Pick a sensible default viz for a result among the eligible ones:
 * temporal → line, 2+ dimensions → stacked column, 1 dimension → column,
 * otherwise table.
 */
export function pickDefault(shape: DataShape, capabilities: VizCapability[]): VizId {
  const eligible = new Set(capabilities.filter((c) => evaluate(c, shape).ok).map((c) => c.id));
  const prefer = (id: VizId): boolean => eligible.has(id);

  // Single aggregate value (no dimension) → headline number.
  if (shape.groupCount === 0 && shape.measureCount > 0 && prefer('kpi')) return 'kpi';

  if (shape.measureCount > 0 && shape.groupCount > 0 && shape.rowCount > 1) {
    if (shape.temporalKey && prefer('line')) return 'line';
    if (shape.groupCount >= 2 && prefer('stackedColumn')) return 'stackedColumn';
    if (prefer('column')) return 'column';
  }
  return 'table';
}
