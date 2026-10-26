import { describe, expect, it } from 'vitest';

import { connectorsRegistryPath, registrySearchOf } from './registryView';

describe('the /connectors registry link (D-124)', () => {
  it('goes there and back as the same search', () => {
    const path = connectorsRegistryPath('io.example/weather tool');
    expect(path).toBe('/connectors?registry=io.example%2Fweather%20tool');
    expect(registrySearchOf(new URL(path, 'https://x').searchParams)).toBe(
      'io.example/weather tool',
    );
  });

  it('is cut as the search box cuts it', () => {
    const params = new URLSearchParams({ registry: ` a\nb${'c'.repeat(80)} ` });
    expect(registrySearchOf(params)).toBe(`ab${'c'.repeat(48)}`);
  });

  it('is null when the address asks for none', () => {
    expect(registrySearchOf(new URLSearchParams('x=1'))).toBeNull();
    expect(registrySearchOf(new URLSearchParams('registry='))).toBe('');
  });
});
