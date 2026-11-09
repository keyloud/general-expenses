import type { Item, PersonId, Assignments } from './receipt';

export type Receipt = {
  id: string;
  title: string;
  createdAt: string;
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

export async function requestJson<T>(path: string, method = 'GET', body?: object, signal?: AbortSignal): Promise<T> {
  // Ограничиваем ожидание. При потере ответа запись могла сохраниться в базе:
  // интерфейс предложит обновить чек, а версия защитит от повторного применения.
  const response = await fetch(`/api${path}`, {
    method,
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
    signal: signal ? AbortSignal.any([signal, AbortSignal.timeout(10000)]) : AbortSignal.timeout(10000),
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new ApiError(problem?.title ?? 'Сервер недоступен. Проверьте его запуск.', response.status);
  }
  return response.json();
}

export type ReceiptSummary = { id: string; title: string; createdAt: string; participantCount: number; itemCount: number; total: number };
export function requestReceipt(receiptId: string, path = '', method = 'GET', body?: object) {
  return requestJson<Receipt>(`/receipts/${encodeURIComponent(receiptId)}${path}`, method, body);
}
