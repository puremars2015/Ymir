import { brandName, companyNameProblem, faviconProblem } from './branding-rules';

describe('branding rules', () => {
  it('prefixes the company name to the fixed project name', () => {
    expect(brandName('Web-Pro')).toBe('Web-Pro Ymir');
    expect(brandName('  ')).toBe('Ymir');
    expect(brandName(null)).toBe('Ymir');
  });

  it('validates the company name', () => {
    expect(companyNameProblem('')).toBeNull();
    expect(companyNameProblem('x'.repeat(41))).not.toBeNull();
    expect(companyNameProblem('a\nb')).not.toBeNull();
  });

  it('accepts raster images only', () => {
    const png = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0, 0, 0, 0, 0, 0, 0, 0]);
    const svg = new TextEncoder().encode('<svg xmlns="http://www.w3.org/2000/svg"/>');
    expect(faviconProblem(100, png)).toBeNull();
    expect(faviconProblem(100, svg)).not.toBeNull();
    expect(faviconProblem(300 * 1024, png)).not.toBeNull();
  });
});
