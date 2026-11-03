export const priorityLabel = (value: number | null | undefined): string => ({ 1: 'Critical', 2: 'High', 3: 'Medium', 4: 'Low' } as Record<number, string>)[value ?? 0] ?? '';
export const effortLabel = (value: number | string | null | undefined): string => typeof value === 'number' ? `${value}m (legacy)` : value ? value[0].toUpperCase() + value.slice(1) : '';
export const duration = (value: number | string | null | undefined): number | null => typeof value === 'number' ? value : null;
export const parsePriority = (value: string): number | null => {
  const v = value.trim().toLowerCase();
  const names: Record<string, number> = { critical: 1, high: 2, medium: 3, low: 4 };
  return names[v] ?? (/^[1-4]$/.test(v) ? Number(v) : null);
};
