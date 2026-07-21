export type PayUniFormData = Record<string, string>;

export interface SubscriptionStatus {
  isSubscribed: boolean;
  status: 'active' | 'cancelled' | 'expired' | null;
  currentPeriodEnd: string | null;
  monthlyQuota: number;
  currentBalance: number;
}
