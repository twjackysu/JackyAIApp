export interface ConnectorStatus {
  provider: string;
  providerDisplayName: string;
  isConnected: boolean;
  services: string[];
  expiresAt?: string;
  requiresReconnection: boolean;
}

export interface ConnectResponse {
  redirectUrl: string;
}

export interface RefreshResponse {
  message: string;
}

export interface DisconnectResponse {
  message: string;
}
