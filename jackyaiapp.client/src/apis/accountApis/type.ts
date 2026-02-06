export interface User {
  id: string;
  name?: string;
  email?: string;
  creditBalance: number;
  totalCreditsUsed: number;
  lastUpdated: Date;
  isAdmin?: boolean;
}
