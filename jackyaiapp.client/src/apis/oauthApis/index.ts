import { api } from '../api';
import { ApiOkResponse } from '../types';

import type { OAuthAuthorizeRequest, OAuthConsent, OAuthDecision } from './types';

/** The consent page of the site's OAuth sign-in (another AI asking to use the site's MCP server). */
export const oauthApis = api.injectEndpoints({
  endpoints: (builder) => ({
    getOAuthConsent: builder.query<OAuthConsent, OAuthAuthorizeRequest>({
      query: (params) => ({ url: 'oauth/consent', params }),
      transformResponse: (res: ApiOkResponse<OAuthConsent>) => res.data,
    }),
    decideOAuth: builder.mutation<{ redirectUrl: string }, OAuthDecision>({
      query: (body) => ({ url: 'oauth/consent', method: 'POST', body }),
      transformResponse: (res: ApiOkResponse<{ redirectUrl: string }>) => res.data,
    }),
  }),
});

export const { useGetOAuthConsentQuery, useDecideOAuthMutation } = oauthApis;
