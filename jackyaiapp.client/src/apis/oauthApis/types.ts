/**
 * An OAuth authorization request to the site (the site as the sign-in of its own MCP server), with
 * OAuth's own field names, passed on from the page's query as it came.
 */
export interface OAuthAuthorizeRequest {
  response_type?: string;
  client_id?: string;
  redirect_uri?: string;
  code_challenge?: string;
  code_challenge_method?: string;
  state?: string;
  scope?: string;
  resource?: string;
}

/** What the consent page shows. */
export interface OAuthConsent {
  clientName: string;
  /** Where the reader goes back to ("claude.ai"). */
  redirectHost: string;
  /** Whether the AI asked to add or change things too. */
  wantsWrites: boolean;
}

export interface OAuthDecision {
  request: OAuthAuthorizeRequest;
  approve: boolean;
  allowWrites: boolean;
}
