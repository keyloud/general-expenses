import { useEffect, useRef, useState } from 'react';
import { ApiError, requestJson } from './api';
import s from './App.module.css';

const botUrl = 'https://t.me/porovnu_expenses_bot';
const startPattern = /^invite_([a-f0-9]{48})$/;

export function routeTelegramInvite() {
  if (window.location.hash.startsWith('#/')) return;
  // SDK уже прочитал параметры запуска. Переносим приглашение в наш hash-маршрут.
  const launch = new URLSearchParams(window.Telegram?.WebApp.initData);
  const url = new URL(window.location.href);
  // Telegram использует фрагмент для initData; кнопку из чата маршрутизируем по query.
  const receipt = url.searchParams.get('receipt');
  if (receipt && /^[a-zA-Z0-9-]+$/.test(receipt)) {
    url.searchParams.delete('receipt');
    url.hash = '/receipts/' + receipt;
    window.history.replaceState(null, '', url);
    return;
  }
  const token = startPattern.exec(launch.get('start_param') ?? url.searchParams.get('tgWebAppStartParam') ?? '')?.[1];
  if (token) {
    url.searchParams.delete('tgWebAppStartParam');
    url.hash = '/invite/' + token;
    window.history.replaceState(null, '', url);
  }
}

function message(cause: unknown) {
  return cause instanceof Error ? cause.message : 'Не удалось связаться с сервером.';
}

export function IssueInvite({ receiptId, person }: { receiptId: string; person: { id: string; name: string; joined: boolean } }) {
  const [token, setToken] = useState('');
  const link = token ? botUrl + '?start=invite_' + token : '';
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState('');
  const active = useRef<AbortController | null>(null);
  useEffect(() => () => active.current?.abort(), []);
  const app = window.Telegram?.WebApp;
  const nativeShare = Boolean(app?.initData && app.shareMessage && app.isVersionAtLeast?.('8.0'));

  async function invite(replace = false) {
    if (active.current && !active.current.signal.aborted) return;
    const controller = new AbortController();
    const { signal } = controller;
    active.current = controller;
    setBusy(true); setStatus('');
    try {
      let code = token;
      if (!code || replace) {
        const issued = await requestJson<{ token: string }>(
          '/receipts/' + encodeURIComponent(receiptId) + '/participants/' + encodeURIComponent(person.id) + '/invite',
          'POST', undefined, signal);
        code = issued.token;
        setToken(code);
      }
      if (nativeShare && app?.shareMessage && !replace) {
        // После отмены снова готовим сообщение с той же ссылкой. Telegram сам показывает выбор личного чата.
        const prepared = await requestJson<{ id: string }>('/invitations/share', 'POST', { token: code }, signal);
        if (signal.aborted) return;
        const sent = await new Promise<boolean>(resolve => app.shareMessage!(prepared.id, resolve));
        if (!signal.aborted) setStatus(sent ? 'Приглашение отправлено.' : 'Приглашение не отправлено.');
      }
    } catch (cause) {
      if (!signal.aborted) {
        if (cause instanceof ApiError && cause.status === 404) setToken('');
        setStatus(message(cause));
      }
    } finally {
      if (!signal.aborted) { active.current = null; setBusy(false); }
    }
  }
  async function copy() {
    try { await navigator.clipboard.writeText(link); setStatus('Ссылка скопирована.'); }
    catch { setStatus('Выделите ссылку и скопируйте вручную.'); }
  }
  if (person.joined) return <p>Участник уже присоединился.</p>;
  return <div className={s.invitation}>
    {(!link || nativeShare) && <button className={s.addButton} disabled={busy} onClick={() => invite()}>
      {busy ? 'Готовим приглашение…' : link ? 'Выбрать личный чат' : 'Пригласить: ' + person.name}
    </button>}
    {link && <details open={!nativeShare || undefined}><summary>Ссылка для {person.name}</summary>
      <label>Ссылка для {person.name}<input value={link} readOnly onFocus={e => e.target.select()} /></label>
      <button className={s.addButton} onClick={copy}>Скопировать ссылку</button>
      <button className={s.cancelEdit} disabled={busy} onClick={() => invite(true)}>Заменить ссылку</button>
      <small>Бот добавит чек после нажатия «Старт». Ссылка действует 48 часов.</small></details>}
    {status && <p role="status">{status}</p>}
  </div>;
}

export function PasteInvite() {
  const [value, setValue] = useState('');
  const [error, setError] = useState('');
  return <details className={s.invitation}><summary>У меня есть приглашение</summary>
    <form onSubmit={e => {
      e.preventDefault();
      let token;
      try {
        const url = new URL(value.trim());
        if (url.origin + url.pathname === botUrl) token = startPattern.exec(url.searchParams.get('start') ?? url.searchParams.get('startapp') ?? '')?.[1];
      } catch { /* Показать подсказку под полем. */ }
      if (token) window.location.hash = '/invite/' + token;
      else setError('Вставьте полную ссылку, полученную от владельца.');
    }}>
      <label>Ссылка на приглашение<input required value={value} onChange={e => setValue(e.target.value)} /></label>
      <button className={s.addButton}>Открыть приглашение</button>{error && <p role="alert">{error}</p>}
    </form></details>;
}

export function JoinPage({ token }: { token: string }) {
  const [info, setInfo] = useState<{title: string; participantName: string} | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    const abort = new AbortController();
    requestJson<{title: string; participantName: string}>('/invitations/preview', 'POST', {token}, abort.signal)
      .then(setInfo).catch(cause => { if (!abort.signal.aborted) setError(message(cause)); });
    return () => abort.abort();
  }, [token]);
  async function accept() {
    setBusy(true); setError('');
    try {
      const { receiptId } = await requestJson<{receiptId: string}>('/invitations/accept', 'POST', {token});
      window.location.replace('#/receipts/' + encodeURIComponent(receiptId));
    } catch (cause) { setError(message(cause)); }
    finally { setBusy(false); }
  }
  return <main className={s.page}><a className={s.backLink} href="#/">← Мои счета</a>
    <section className={s.loginCard}><span className={s.eyebrow}>Приглашение в чек</span>
      <h1>{info?.title ?? 'Приглашение'}</h1>
      {info && <><p>Вы сможете отмечать свои блюда и видеть суммы всех участников.</p>
        <button className={s.addButton} disabled={busy} onClick={accept}>Присоединиться как {info.participantName}</button></>}
      {!info && !error && <p role="status">Загружаем приглашение…</p>}
      {error && <p role="alert">{error}</p>}
    </section></main>;
}
