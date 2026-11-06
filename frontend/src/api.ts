import type { Item, PersonId, Assignments } from './receipt';

export type Receipt = {
  participants: { id: PersonId; name: string; initial: string }[];
  items: Item[];
  assignments: Assignments;
  result: { totals: Record<PersonId, number>; unassigned: number; total: number };
  revision: number;
  instanceId: string;
  deleted: { item: Item; index: number; owners: PersonId[] } | null;
};

export class ApiError extends Error {
  constructor(message: string, public status: number) { super(message); }
}

export async function requestReceipt(path = '', method = 'GET', body?: object): Promise<Receipt> {
  // Ограничиваем ожидание. При потере ответа запись могла сохраниться в базе:
  // интерфейс предложит обновить чек, а версия защитит от повторного применения.
  const response = await fetch(`/api/receipt${path}`, {
    method,
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
    signal: AbortSignal.timeout(10000),
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new ApiError(problem?.title ?? 'Сервер недоступен. Проверьте его запуск.', response.status);
  }
  return response.json();
}
