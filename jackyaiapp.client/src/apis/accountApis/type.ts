export interface User {
  id: string;
  name?: string;
  email?: string;
  lastUpdated: Date;
  isAdmin?: boolean;
}
