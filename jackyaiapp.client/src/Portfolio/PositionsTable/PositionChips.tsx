import Chip from '@mui/material/Chip';

import { exposureChipOf, typeChipOf } from './positionDisplay';

import { PositionDto } from '@/apis/portfolioApis/types';

const CHIP_SX = { fontSize: 10, height: 18 } as const;

/** The kind of holding, and its exposure when it isn't 1× — the same in the table and the cards. */
export function PositionChips({ position }: { position: PositionDto }) {
  const type = typeChipOf(position);
  const exposure = exposureChipOf(position);
  return (
    <>
      <Chip label={type.label} size="small" color={type.color} variant="outlined" sx={CHIP_SX} />
      {exposure && (
        <Chip
          label={exposure.label}
          size="small"
          color={exposure.color}
          variant="outlined"
          sx={CHIP_SX}
        />
      )}
    </>
  );
}
