import { Code, Hub, ShowChart, Storage, WorkOutlined } from '@mui/icons-material';

import type { ConnectorCategory } from '@/apis/connectorsApis/types';
import type { SvgIconProps } from '@mui/material';

const ICONS: Record<ConnectorCategory, typeof Hub> = {
  site: Hub,
  data: Storage,
  finance: ShowChart,
  dev: Code,
  productivity: WorkOutlined,
};

/** A connector's picture: its category's icon (a connector's own logo is not ours to draw). */
export default function CategoryIcon({
  category,
  ...props
}: { category: ConnectorCategory } & SvgIconProps) {
  const Icon = ICONS[category];
  return <Icon aria-hidden {...props} />;
}
