export interface CreditBalanceResponse {
  balance: number;
}

export interface CreditTransaction {
  id: number;
  amount: number;
  balanceAfter: number;
  transactionType: 'initial' | 'consume' | 'topup' | 'refund' | 'bonus';
  reason: string;
  description?: string;
  createdAt: string;
}

export interface CreditHistoryResponse {
  transactions: CreditTransaction[];
  pagination: {
    pageNumber: number;
    pageSize: number;
    totalCount: number;
    totalPages: number;
  };
}

export interface CreditCheckResponse {
  hasSufficient: boolean;
  balance: number;
  required: number;
}

export interface GetHistoryRequest {
  pageNumber?: number;
  pageSize?: number;
}
