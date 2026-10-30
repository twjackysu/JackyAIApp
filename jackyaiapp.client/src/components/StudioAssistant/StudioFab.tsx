import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import Fab from '@mui/material/Fab';

import { ACCENT_GRADIENT } from '@/components/aiChat/style';
import { ActionTooltip } from '@/components/hints';

interface Props {
  onClick: () => void;
  /** Hidden while what it opens is open (the drawer covers that corner). */
  hidden?: boolean;
}

/** Studio AI's round button in the lower right corner of every page it is on (design/05 §1). */
export default function StudioFab({ onClick, hidden = false }: Props) {
  return (
    <ActionTooltip title="Studio AI（⌘K）">
      <Fab
        aria-label="開啟 Studio AI"
        onClick={onClick}
        sx={{
          position: 'fixed',
          right: { xs: 16, sm: 24 },
          bottom: { xs: 'calc(16px + env(safe-area-inset-bottom))', sm: 24 },
          zIndex: (theme) => theme.zIndex.speedDial,
          color: '#fff',
          backgroundImage: ACCENT_GRADIENT,
          display: hidden ? 'none' : undefined,
        }}
      >
        <AutoAwesomeIcon />
      </Fab>
    </ActionTooltip>
  );
}
