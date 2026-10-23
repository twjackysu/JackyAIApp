import type { Pixel } from './overlayGeometry';

/** A price (and maybe a date) by a point, haloed so it reads over the candles. */
export function PriceLabel({
  at,
  plotRight,
  color,
  halo,
  text,
}: {
  at: Pixel;
  plotRight: number;
  color: string;
  halo: string;
  text: string;
}) {
  // Near the right edge (where a line in force ends) the label goes to the left of the point.
  const left = at.x > plotRight - 80;
  return (
    <text
      x={at.x + (left ? -10 : 10)}
      y={at.y - 10}
      textAnchor={left ? 'end' : 'start'}
      fill={color}
      stroke={halo}
      strokeWidth={3}
      paintOrder="stroke"
      fontSize={12}
      fontWeight={600}
    >
      {text}
    </text>
  );
}
