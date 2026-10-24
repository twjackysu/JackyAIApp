import SubdirectoryArrowRightIcon from '@mui/icons-material/SubdirectoryArrowRight';
import { Box, ButtonBase, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';

import { ACCENT, accentWash, hairline } from '../style';

/** Questions to carry on with, under the last answer (`utils/followUps`); a press sends one. */
export default function FollowUps({
  texts,
  onPick,
}: {
  texts: readonly string[];
  onPick: (text: string) => void;
}) {
  const theme = useTheme();
  if (texts.length === 0) return null;
  return (
    <Box
      role="group"
      aria-label="接著問"
      data-testid="follow-ups"
      sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.75, mt: 1.25 }}
    >
      {texts.map((text) => (
        <ButtonBase
          key={text}
          onClick={() => onPick(text)}
          sx={{
            gap: 0.5,
            px: 1.25,
            py: 0.5,
            borderRadius: 99,
            border: `1px solid ${hairline(theme)}`,
            color: 'text.secondary',
            transition: 'border-color 120ms, background-color 120ms, color 120ms',
            '&:hover, &.Mui-focusVisible': {
              borderColor: ACCENT,
              color: 'text.primary',
              bgcolor: accentWash(theme, 0.08),
            },
          }}
        >
          <SubdirectoryArrowRightIcon sx={{ fontSize: 15, color: ACCENT }} />
          <Typography variant="body2" sx={{ fontSize: 13 }}>
            {text}
          </Typography>
        </ButtonBase>
      ))}
    </Box>
  );
}
