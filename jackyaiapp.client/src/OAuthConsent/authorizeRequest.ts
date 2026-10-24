import type { OAuthAuthorizeRequest } from '@/apis/oauthApis/types';

const FIELDS = [
  'response_type',
  'client_id',
  'redirect_uri',
  'code_challenge',
  'code_challenge_method',
  'state',
  'scope',
  'resource',
] as const;

/** The OAuth fields of the page's query (anything else in it is left out); the server checks them all. */
export function authorizeRequestOf(search: string): OAuthAuthorizeRequest {
  const params = new URLSearchParams(search);
  const request: OAuthAuthorizeRequest = {};
  for (const field of FIELDS) {
    const value = params.get(field);
    if (value !== null) request[field] = value;
  }
  return request;
}
