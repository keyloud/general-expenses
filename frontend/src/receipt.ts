// Формат данных интерфейса. Демочек и окончательные расчёты теперь находятся в C#.
// Участники теперь создаются для конкретного счёта и получают собственные ID.
export type PersonId = string;
export type Item = { id: string; name: string; detail: string; amount: number };
export type Assignments = Record<string, PersonId[]>;

// В интерфейсе только форматируем полученные копейки, не пересчитывая доли участников.
export const money = (amount: number) => new Intl.NumberFormat('ru-RU', {
  style: 'currency', currency: 'RUB', maximumFractionDigits: amount % 100 ? 2 : 0,
}).format(amount / 100);
