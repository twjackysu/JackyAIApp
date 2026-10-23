import { describe, expect, it } from 'vitest';

import { allowedPermissions, defaultPermission, permissionCounts } from './toolPermission';

describe('allowedPermissions', () => {
  it('a tool that cannot be undone never runs unasked', () => {
    expect(allowedPermissions('destructive')).toEqual(['confirm', 'disabled']);
  });

  it('reading and writing can take any of the three', () => {
    expect(allowedPermissions('read')).toEqual(['auto', 'confirm', 'disabled']);
    expect(allowedPermissions('write')).toEqual(['auto', 'confirm', 'disabled']);
  });
});

describe('defaultPermission', () => {
  it('reading runs on its own, anything else asks first', () => {
    expect(defaultPermission('read')).toBe('auto');
    expect(defaultPermission('write')).toBe('confirm');
    expect(defaultPermission('destructive')).toBe('confirm');
  });
});

describe('permissionCounts', () => {
  it('counts each permission, those with none at zero', () => {
    expect(permissionCounts(['auto', 'confirm', 'auto'])).toEqual({
      auto: 2,
      confirm: 1,
      disabled: 0,
    });
  });
});
