// Разбираем строку по частям, чтобы не умножать дробное число на 100.
// Верхний предел одной позиции: 1 000 000 рублей.
export function parsePrice(value: string): number | null {
  const normalized = value.trim().replace(',', '.');
  if (!/^\d+(?:\.\d{1,2})?$/.test(normalized)) return null;
  const [rubles, kopecks = ''] = normalized.split('.');
  const amount = Number(rubles) * 100 + Number(kopecks.padEnd(2, '0'));
  return Number.isSafeInteger(amount) && amount > 0 && amount <= 100_000_000 ? amount : null;
}
