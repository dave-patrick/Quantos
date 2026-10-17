import type { FieldDef } from './supertags.svelte';

function isEmpty(value: unknown) {
  return value === null || value === undefined || (typeof value === 'string' && value.trim() === '') || (Array.isArray(value) && value.length === 0);
}

function realDate(text: string) {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(text);
  if (!m) return false;
  const d = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
  return d.getFullYear() === Number(m[1]) && d.getMonth() === Number(m[2]) - 1 && d.getDate() === Number(m[3]);
}

/**
 * What's wrong with a field's value, or null. One set of rules for both places (BUILD-PLAN 8C.4-5):
 * forms refuse to create until every problem is fixed; the properties panel only shows them.
 */
export function fieldProblem(field: FieldDef, value: unknown): string | null {
  if (isEmpty(value)) return field.required ? 'Required' : null;
  const values = Array.isArray(value) ? value.map(String) : [String(value)];
  const text = values[0].trim();
  switch (field.type) {
    case 'number':
      return Number.isNaN(Number(text)) ? 'Enter a number' : null;
    case 'date':
      return /^\d{4}-\d{2}-\d{2}$/.test(text) && realDate(text) ? null : 'Enter a date as YYYY-MM-DD';
    case 'datetime':
      return /^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}/.test(text) && realDate(text) ? null : 'Enter a date and time';
    case 'url':
      return /^https?:\/\/\S+$/.test(text) ? null : 'Enter a web address starting with http:// or https://';
    case 'email':
      return /^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(text) ? null : 'Enter an email address';
    case 'select':
    case 'status':
    case 'multiselect': {
      if (!field.options?.length) return null;
      const unknown = values.filter((v) => !field.options!.includes(v.trim()));
      return unknown.length ? `Choose from: ${field.options.join(', ')}` : null;
    }
    default:
      return null;
  }
}