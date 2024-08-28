export interface CheckAuthResponse {
  isAuthenticated: boolean;
}
export interface User {
  id: string;
  partitionKey: string;
  name?: string;
  email?: string;
  creditBalance: number;
  totalCreditsUsed: number;
  lastUpdated: Date;
  wordIds: string[];
  isAdmin?: boolean;
}
