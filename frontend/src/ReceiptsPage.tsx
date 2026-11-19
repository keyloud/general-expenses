import { useEffect, useRef, useState, type FormEvent } from 'react';
import { ApiError, requestJson, type Receipt, type ReceiptSummary } from './api';
import { money } from './receipt';
import s from './App.module.css';

export function ReceiptsPage() {
  const [receipts, setReceipts] = useState<ReceiptSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [refresh, setRefresh] = useState(0);
  const [creating, setCreating] = useState(false);
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true); setError('');
    requestJson<ReceiptSummary[]>('/receipts', 'GET', undefined, controller.signal)
      .then(data => { if (!controller.signal.aborted) setReceipts(data); })
      .catch(() => { if (!controller.signal.aborted) setError('Не удалось загрузить счета. Проверьте связь и попробуйте ещё раз.'); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    // Ушли на другой экран — поздний ответ уже не должен менять список.
    return () => controller.abort();
  }, [refresh]);
  return <main className={s.page}>
    <header className={s.header}><a className={s.brand} href="#/"><span className={s.logo}>÷</span> поровну</a><span className={s.demo}>Расходы с друзьями</span></header>
    <div className={s.listIntro}><div><span className={s.eyebrow}>Каждой компании — свой счёт</span><h1>Мои счета</h1><p>Ужин, кофе или обед с коллегами.<br/>Сохраняйте отдельно, делите вместе.</p></div>
      {!creating && <button className={s.addButton} onClick={() => setCreating(true)}>+ Создать счёт</button>}
    </div>
    {creating && <CreateReceiptForm onCancel={() => setCreating(false)} />}
    <div className={s.listToolbar}><span>{loading ? 'Загружаем счета…' : `Всего счетов: ${receipts.length}`}</span><button disabled={loading} onClick={() => setRefresh(n => n + 1)}>Обновить список</button></div>
    {error && <div className={s.errorBox} role="alert">{error}<button onClick={() => setRefresh(n => n + 1)}>Повторить загрузку</button></div>}
    {!loading && !error && receipts.length === 0 && <div className={s.emptyState}><h2>Первый счёт — начало истории</h2><p>Нажмите «Создать счёт», укажите название и участников.</p></div>}
    <div className={s.receiptGrid}>{receipts.map(receipt => <a key={receipt.id} className={s.receiptCard} href={`#/receipts/${encodeURIComponent(receipt.id)}`}>
      <div className={s.cardTop}><span className={s.eyebrow}>{receipt.itemCount ? 'Совместный счёт' : 'Готов к первым позициям'}</span><span aria-hidden="true">↗</span></div>
      <h2>{receipt.title}</h2><p>Участников: {receipt.participantCount} · Позиций: {receipt.itemCount}</p>
      <div className={s.cardBottom}><time dateTime={receipt.createdAt}>{new Date(receipt.createdAt).toLocaleDateString('ru-RU', {day:'numeric',month:'long',year:'numeric'})}</time><strong>{money(receipt.total)}</strong></div>
    </a>)}</div>
    <footer className={s.footer}>Эти счета видны только вам. Приглашение друзей появится на следующем этапе.</footer>
  </main>;
}

function CreateReceiptForm({ onCancel }: { onCancel: () => void }) {
  const [title, setTitle] = useState('');
  const [names, setNames] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const sending = useRef(false);
  // Один ID на попытку создания. Повтор после потери ответа не создаёт дубликат.
  const [requestId] = useState(() => crypto.randomUUID());
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending.current) return;
    const participants = names.split(/\r?\n/).map(name => name.trim()).filter(Boolean);
    if (!title.trim() || participants.length < 1 || participants.length > 12 || participants.some(n => n.length > 100)
      || new Set(participants.map(n => n.toLowerCase())).size !== participants.length) {
      setError('Укажите название и от 1 до 12 разных имён участников, каждое не длиннее 100 символов.'); return;
    }
    sending.current = true; setBusy(true); setError('');
    try {
      const receipt = await requestJson<Receipt>('/receipts', 'POST', { requestId, title: title.trim(), participants });
      window.location.hash = `/receipts/${receipt.id}`;
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.message : 'Нет связи с сервером. Данные формы сохранены — можно повторить попытку.');
    } finally { sending.current = false; setBusy(false); }
  }
  return <form className={s.createReceipt} onSubmit={submit} aria-labelledby="create-title">
    <fieldset disabled={busy} className={s.receiptFields}>
      <span className={s.eyebrow}>Новая история</span><h2 id="create-title">Создать счёт</h2>
      <div className={s.createFields}>
        <div><label htmlFor="receipt-title">Название счёта</label><input autoFocus id="receipt-title" value={title} maxLength={100} required placeholder="Например, ужин в пятницу" onChange={e => setTitle(e.target.value)} /><p>Название поможет найти счёт в списке.</p></div>
        <div><label htmlFor="receipt-people">Участники — каждый с новой строки</label><textarea id="receipt-people" value={names} required rows={4} maxLength={1300} placeholder={'Аня\nБорис\nВика'} onChange={e => setNames(e.target.value)} /><p>От 1 до 12 человек. Добавьте и себя.</p></div>
      </div>
      {error && <p className={s.error} role="alert">{error}</p>}
      <button type="submit" className={s.addButton}>{busy ? 'Создаём…' : 'Создать и открыть'}</button><button type="button" className={s.cancelEdit} onClick={onCancel}>Отмена</button>
    </fieldset>
  </form>;
}
