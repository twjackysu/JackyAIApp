import { ChartViz } from './renderers/ChartViz';
import { HierarchyViz } from './renderers/HierarchyViz';
import { KpiViz } from './renderers/KpiViz';
import { SankeyViz } from './renderers/SankeyViz';
import { TableViz } from './renderers/TableViz';
import { WaterfallViz } from './renderers/WaterfallViz';

import type { DataShape, VizId } from './types';

/**
 * Renders a specific visualization for a result, using the authoritative column
 * classification from the shape (hint-aware, so numeric dimensions like
 * DateTrunc/Year aren't treated as measures). Shared by the in-page VizSwitcher
 * and saved dashboard widgets.
 */
export function renderViz(
  id: VizId,
  rows: Record<string, unknown>[],
  headers: string[],
  shape: DataShape,
) {
  const { groupKeys, measureKeys, temporalKey } = shape;
  switch (id) {
    case 'table':
      return <TableViz rows={rows} headers={headers} />;
    case 'kpi':
      return <KpiViz rows={rows} measureKeys={measureKeys} temporalKey={temporalKey} />;
    case 'treemap':
    case 'sunburst':
    case 'tree':
      return <HierarchyViz mode={id} rows={rows} groupKeys={groupKeys} measureKeys={measureKeys} />;
    case 'waterfall':
      return <WaterfallViz rows={rows} measureKeys={measureKeys} temporalKey={temporalKey} />;
    case 'sankey':
      return <SankeyViz rows={rows} groupKeys={groupKeys} measureKeys={measureKeys} />;
    default:
      // column / bar / line / pie / donut / stackedColumn / stackedBar
      return (
        <ChartViz chartType={id} rows={rows} groupKeys={groupKeys} measureKeys={measureKeys} />
      );
  }
}
