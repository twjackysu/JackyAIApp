export const formatNumber = (n: number): string => n.toLocaleString('zh-TW');
export const formatBillion = (n: number): string => `${(n / 1_000_000_000).toFixed(1)}B`;
export const formatVolumeBillion = (n: number): string => `${(n / 100_000_000).toFixed(1)}億`;

/** Convert 民國 date "1150211" to "02/11" display */
export const formatROCDate = (d: string): string => {
  if (d.length < 7) return d;
  return `${d.slice(3, 5)}/${d.slice(5, 7)}`;
};
