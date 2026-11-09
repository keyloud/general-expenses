import { useEffect, useRef, useState } from 'react';
import { ApiError, requestReceipt, type Receipt } from './api';

export function useReceipt(receiptId: string) {
  const [receipt, setReceipt] = useState<Receipt | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  // Ref блокирует второй запрос сразу, ещё до следующей отрисовки React.
  const inFlight = useRef(false);

  async function perform(path = '', method = 'GET', data?: object): Promise<boolean> {
    if (inFlight.current) return false;
    inFlight.current = true;
    setBusy(true);
    setError('');
    try {
      const body = method === 'GET' ? undefined : {
        ...data, revision: receipt?.revision, instanceId: receipt?.instanceId,
      };
      const next = await requestReceipt(receiptId, path, method, body);
      // Список, участники и итог заменяются вместе одним подтверждённым снимком сервера.
      setReceipt(next);
      return true;
    } catch (cause) {
      const message = cause instanceof ApiError ? cause.message : 'Нет связи с сервером.';
      setError(`${message} Обновите чек перед повтором. Введённые данные сохранены.`);
      return false;
    } finally {
      inFlight.current = false;
      setBusy(false);
    }
  }

  // Начальная загрузка. Повторный вызов эффекта в StrictMode отсечёт inFlight.
  useEffect(() => { void perform(); }, []);
  return { receipt, busy, error, perform };
}
