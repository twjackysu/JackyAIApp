export interface CreditPack {
  id: string;
  name: string;
  priceInCents: number;
  credits: number;
  badge: string | null;
}

export interface CheckoutResponse {
  checkoutUrl: string;
}
