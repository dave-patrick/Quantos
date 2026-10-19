import { describe, expect, it } from 'vitest';
import { parseCsv, parsePasted, toCsv, typedValue } from '../src/lib/query';

describe('CSV', () => {
  it('keeps line breaks, separators and quotes inside quoted cells', () => {
    expect(parseCsv('title,notes\r\n"Budget","line one\nline two, with comma"\r\n"Say ""hi""",x\r\n')).toEqual([
      ['title', 'notes'], ['Budget', 'line one\nline two, with comma'], ['Say "hi"', 'x']
    ]);
  });
  it('reads semicolon files from European Excel and skips the UTF-8 marker', () => {
    expect(parsePasted('﻿Title;Amount\nRent;1200,50\n')).toEqual({ headers: ['Title', 'Amount'], rows: [['Rent', '1200,50']] });
  });
  it('writes CSV that reads back the same', () => {
    const rows = [['Q4, budget', 'say "hi"'], ['multi\nline', '']];
    const csv = toCsv(['title', 'notes'], rows);
    expect(csv.startsWith('﻿')).toBe(true);
    expect(csv).toContain('\r\n');
    expect(parseCsv(csv.slice(1))).toEqual([['title', 'notes'], ...rows.map((r) => r.map((c) => c))].filter((r) => r.some((c) => c !== '')));
  });
  it('types plain numbers and booleans only', () => {
    expect(typedValue('42')).toBe(42);
    expect(typedValue('-3.5')).toBe(-3.5);
    expect(typedValue('007')).toBe('007');
    expect(typedValue('TRUE')).toBe(true);
    expect(typedValue('2026-10-18')).toBe('2026-10-18');
    expect(typedValue('1,200')).toBe('1,200');
  });
});
