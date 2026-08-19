export interface DrawdownStatus {
  label: string;
  color: 'success' | 'warning';
}

/**
 * "Where am I RIGHT NOW relative to the running peak?" — deliberately distinct from
 * DrawdownAnalysis.stillUnderwater, which answers the narrower question "did the single worst
 * episode in this window eventually recover?".
 *
 * Those two diverge in a common and confusing case: net worth falls hard, recovers to a new high
 * (so the worst episode IS resolved), then drifts down a little again. stillUnderwater is false,
 * but you are not at your peak. Labelling that state "已復原" next to a "目前回撤 −3%" reading is
 * self-contradictory, so the prominent badge reports current position and the historical
 * episode's resolution is left to the 高點 → 低點 → 復原於 line beneath it.
 *
 * currentDrawdownPct arrives already rounded to 2dp from the server, so an exact 0 comparison is
 * safe here — no float-epsilon dance needed.
 */
export function currentDrawdownStatus(currentDrawdownPct: number): DrawdownStatus {
  return currentDrawdownPct < 0
    ? { label: '低於前高', color: 'warning' }
    : { label: '目前位於高點', color: 'success' };
}
