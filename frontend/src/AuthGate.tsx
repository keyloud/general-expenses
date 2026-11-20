import { useEffect, useState, type FormEvent, type ReactNode } from 'react';
import { ApiError, requestJson } from './api';
import s from './App.module.css';

type User = { id: string; name: string; username: string | null; isLocal: boolean };
declare global {
  interface Window {
    Telegram?: { WebApp: {
      initData: string; ready(): void; expand(): void;
      isVersionAtLeast?(version: string): boolean;
      shareMessage?(id: string, callback: (sent: boolean) => void): void;
    } };
  }
}

// StrictMode повторно запускает effects в разработке. Один общий Promise не даёт
// отправить два входа одновременно; cookie хранит браузер, а не localStorage.
let initialLogin: Promise<User> | undefined;
async function enter(): Promise<User> {
  const telegram = window.Telegram?.WebApp;
  telegram?.ready();
  if (!telegram?.initData) return requestJson<User>('/auth/me');
  telegram.expand();
  // После перезагрузки initData может быть старше пяти минут, но сессия ещё жива.
  // Этот ID служит только для сравнения; право доступа по-прежнему даёт серверная cookie.
  let expectedId: string | undefined;
  try { expectedId = String(JSON.parse(new URLSearchParams(telegram.initData).get('user') ?? '{}').id); }
  catch { /* Неверные данные далее отклонит сервер. */ }
  try {
    const existing = await requestJson<User>('/auth/me');
    if (!existing.isLocal && existing.id === expectedId) return existing;
  } catch (cause) {
    if (!(cause instanceof ApiError) || cause.status !== 401) throw cause;
  }
  return requestJson<User>('/auth/telegram', 'POST', { initData: telegram.initData });
}
export function AuthGate({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [key, setKey] = useState('');
  const [busy, setBusy] = useState(false);
  const inTelegram = Boolean(window.Telegram?.WebApp.initData);
  const localDevelopment = import.meta.env.DEV;
  useEffect(() => {
    let active = true;
    initialLogin ??= enter();
    initialLogin.then(value => { if (active) setUser(value); }).catch(cause => {
      if (active && (inTelegram || !(cause instanceof ApiError) || cause.status !== 401))
        setError(cause instanceof ApiError ? cause.message : 'Не удалось связаться с сервером.');
    }).finally(() => { if (active) setLoading(false); });
    const expired = () => { setUser(null); setError('Сессия закончилась. Войдите снова.'); };
    window.addEventListener('session-expired', expired);
    return () => { active = false; window.removeEventListener('session-expired', expired); };
  }, [inTelegram]);

  async function localLogin(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy) return;
    setBusy(true); setError('');
    try { setUser(await requestJson<User>('/auth/local', 'POST', { key: key.trim() })); setKey(''); }
    catch (cause) { setError(cause instanceof ApiError ? cause.message : 'Не удалось связаться с сервером.'); }
    finally { setBusy(false); }
  }
  async function logout() {
    setBusy(true); setError('');
    try { await requestJson('/auth/logout', 'POST'); setUser(null); initialLogin = undefined; }
    catch { setError('Не удалось выйти. Попробуйте ещё раз.'); }
    finally { setBusy(false); }
  }
  if (user) return <>
    <div className={s.accountBar}><span>{user.name}{user.isLocal ? ' · локальный аккаунт' : ''}</span>
      <button disabled={busy} onClick={logout}>Выйти</button>{error && <span role="alert">{error}</span>}</div>
    {children}
  </>;
  return <main className={s.page}>
    <header className={s.header}><a className={s.brand} href="#/"><span className={s.logo}>÷</span> поровну</a></header>
    <section className={s.loginCard} aria-labelledby="login-title">
      <span className={s.eyebrow}>Своя компания. Свои счета.</span>
      <h1 id="login-title">Всё начинается<br/>с одного ужина.</h1>
      <p>Сохраняйте общие расходы и делите чек по выбранным блюдам.</p>
      {loading ? <p role="status">Проверяем вход…</p> : <>
        {inTelegram ? <><p>Для повторного входа закройте Mini App и откройте его заново.</p>
          <button className={s.addButton} onClick={() => window.location.reload()}>Повторить подключение</button></> :
          <><p>{localDevelopment ? 'Откройте приложение через Telegram или войдите в локальный аккаунт разработчика.' : 'Откройте приложение кнопкой меню в чате с ботом.'}</p>
            <a className={s.backLink} href="https://t.me/porovnu_expenses_bot" target="_blank" rel="noreferrer">Перейти к боту ↗</a>
            {localDevelopment && <details className={s.localLogin}><summary>Локальная разработка</summary>
              <form onSubmit={localLogin}>
                <label htmlFor="local-key">Ключ локального входа</label>
                <input id="local-key" type="password" autoComplete="off" required maxLength={256}
                  value={key} onChange={event => setKey(event.target.value)} disabled={busy}/>
                <p>Ключ находится в файле .local/development-login.key в папке проекта.</p>
                <button className={s.addButton} disabled={busy}>{busy ? 'Входим…' : 'Войти локально'}</button>
              </form>
            </details>}</>}
        {error && <p className={s.errorBox} role="alert">{error}</p>}
      </>}
    </section>
  </main>;
}
