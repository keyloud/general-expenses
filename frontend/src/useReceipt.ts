import { useEffect, useRef, useState } from 'react';
import { ApiError, requestJson, requestReceipt, type Receipt } from './api';

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
      setError(cause instanceof ApiError && cause.status < 500 ? message : `${message} Обновите чек перед повтором. Введённые данные сохранены.`);
      return false;
    } finally {
      inFlight.current = false;
      setBusy(false);
    }
  }

  // Начальная загрузка. Повторный вызов эффекта в StrictMode отсечёт inFlight.
  useEffect(() => { void perform(); }, []);
  // У других участников меняются и свободные порции, и статусы подтверждения.
  // Фоновая загрузка не блокирует форму и не затирает более свежий ответ на запись.
  useEffect(() => {
    const controller = new AbortController();
    let reading = false;
    async function refresh() {
      if (document.hidden || inFlight.current || reading) return;
      reading = true;
      try {
        const next = await requestJson<Receipt>('/receipts/' + encodeURIComponent(receiptId), 'GET', undefined, controller.signal);
        if (!controller.signal.aborted && !inFlight.current)
          setReceipt(old => old && next.revision > old.revision ? next : old);
      } catch { /* Кнопка обновления позволяет повторить запрос с сообщением об ошибке. */ }
      finally { reading = false; }
    }
    const timer = window.setInterval(refresh, 6000);
    return () => { clearInterval(timer); controller.abort(); };
  }, [receiptId]);
  return { receipt, busy, error, perform };
}
