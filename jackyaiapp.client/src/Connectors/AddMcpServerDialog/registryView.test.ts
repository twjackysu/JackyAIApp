import { describe, expect, it } from 'vitest';

import { registryCredentials, registryHost, registryNeeds, registryNotice } from './registryView';

import type { McpRegistryServer } from '@/apis/connectorsApis/types';

const server = (patch: Partial<McpRegistryServer> = {}): McpRegistryServer => ({
  name: 'io.example/weather',
  title: 'Weather',
  description: 'Forecasts.',
  url: 'https://mcp.weather.example/mcp',
  websiteUrl: null,
  repositoryUrl: null,
  version: '1.0.0',
  requiredHeaders: [],
  catalogKey: null,
  ...patch,
});

describe('registryCredentials', () => {
  it('starts a server that asks for Authorization with a token', () => {
    expect(registryCredentials(server({ requiredHeaders: ['X-Other', 'authorization'] }))).toEqual({
      kind: 'bearer',
    });
  });

  it('starts one that names another header with an API key in it', () => {
    expect(registryCredentials(server({ requiredHeaders: ['X-API-Key'] }))).toEqual({
      kind: 'apiKey',
      headerName: 'X-API-Key',
    });
  });

  it('leaves the form as it is when it names nothing', () => {
    expect(registryCredentials(server())).toEqual({});
  });
});

describe('registryNeeds', () => {
  it('says what the reader has to bring', () => {
    expect(registryNeeds(server({ requiredHeaders: ['Authorization'] }))).toBe('需要 token');
    expect(registryNeeds(server({ requiredHeaders: ['X-API-Key'] }))).toBe(
      '需要 API key（X-API-Key）',
    );
    expect(registryNeeds(server())).toBeNull();
  });
});

describe('registryHost', () => {
  it('is the host of its address', () => {
    expect(registryHost(server())).toBe('mcp.weather.example');
  });
});

describe('registryNotice', () => {
  it('says nothing was found for the search, and that only names are matched', () => {
    expect(registryNotice({ servers: [], more: false }, ' 天氣 ')).toContain('名稱含「天氣」');
  });

  it('says only the first ones are listed when there are more', () => {
    expect(registryNotice({ servers: [server()], more: true }, 'w')).toBe(
      '只列出前 1 個，輸入更多字可以縮小範圍。',
    );
  });

  it('is quiet when everything found is shown', () => {
    expect(registryNotice({ servers: [server()], more: false }, 'w')).toBeNull();
  });
});
