const money = new Intl.NumberFormat(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const hours = new Intl.NumberFormat(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 2 });

export const formatMoney = (value: number | null | undefined): string => (value === null || value === undefined ? '–' : money.format(value));
export const formatHours = (value: number): string => hours.format(value);
export const formatPercent = (value: number): string => `${hours.format(value)}%`;

/** Renders an ISO UTC timestamp as "yyyy-MM-dd HH:mm UTC". */
export const formatUtc = (value: string | null | undefined): string => (value ? `${value.replace('T', ' ').slice(0, 16)} UTC` : '–');

/** Splits PascalCase enum values into words: "UnderFix" → "Under fix". */
export function humanize(value: string): string {
  const spaced = value.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

export function isMonth(value: string): boolean {
  return /^\d{4}-(0[1-9]|1[0-2])$/.test(value);
}
