export interface User {
  id: string;
  name?: string;
  email?: string;
  lastUpdated: Date;
  isAdmin?: boolean;
  // Portfolio「回落煞車」card is shown only when true (admin, or the server-side Audience=All).
  drawdownBrakeEnabled?: boolean;
}
