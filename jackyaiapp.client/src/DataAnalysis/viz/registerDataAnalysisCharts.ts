import { SankeyChart, SunburstChart, TreeChart, TreemapChart } from 'echarts/charts';

import { echarts } from '@/charts/echarts';

// ── DataAnalysis-only ECharts registration (side effect on import) ────────────
//
// Treemap/sunburst/tree/sankey are only ever rendered by the viz renderers in this folder
// (HierarchyViz, SankeyViz), and they are not cheap — each brings its own layout code, ~84 kB
// raw / ~27 kB gzipped between them. Registering them in @/charts/echarts alongside bar/line/pie
// would put that in the chunk *every* charting route loads, so Portfolio and Finance would
// download four chart types they can never draw.
//
// Keeping the `echarts.use` call here instead means the code lands in the DataAnalysis/Dashboard
// chunk, which is the only place it can run. Import this module for its side effect from any
// renderer that draws one of these four types, before the option reaches <EChart>.
echarts.use([TreemapChart, SunburstChart, TreeChart, SankeyChart]);
