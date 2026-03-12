export interface ECPayPack {
  id: string;
  name: string;
  priceTWD: number;
  credits: number;
  badge: string | null;
}

export type ECPayFormData = Record<string, string>;
