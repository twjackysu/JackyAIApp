export interface ECPayPack {
  id: string;
  name: string;
  priceTWD: number;
  credits: number;
  badge: string | null;
}

export type ECPayFormData = Record<string, string>;

export interface SubscriptionStatus {
  isSubscribed: boolean;
  status: 'active' | 'cancelled' | 'expired' | null;
  currentPeriodEnd: string | null;
  monthlyQuota: number;
  currentBalance: number;
}
