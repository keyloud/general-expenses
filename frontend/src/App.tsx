import { useState } from 'react';
import { money, type PersonId } from './receipt';
import { useReceipt } from './useReceipt';
import { AddItemForm } from './AddItemForm';
import s from './App.module.css';

export function App() {
  const { receipt, busy, error, perform } = useReceipt();
  const [editingId, setEditingId] = useState<string | null>(null);
  const [active, setActive] = useState<PersonId>('anya');
  const [showSummary, setShowSummary] = useState(false);

  // Пока снимка нет, не показываем выдуманные нулевые суммы вместо данных сервера.
  if (!receipt) return <main className={s.page}><h1>Поровну</h1>
    <p role="status">{busy ? 'Загружаем чек…' : 'Чек ещё не загружен.'}</p>
    {error && <p role="alert">{error}</p>}
    <button disabled={busy} onClick={() => void perform()}>Повторить загрузку</button>
  </main>;

  const { items, assignments, result, participants, deleted } = receipt;
  const current = participants.find(p => p.id === active)!;
  async function mutate(path: string, method: string, data?: object) {
    const success = await perform(path, method, data);
    if (success) setShowSummary(false);
    return success;
  }
  async function addItem(name: string, amount: number) {
    return mutate('/items', 'POST', { name, amount });
  }
  async function editItem(id: string, name: string, amount: number) {
    const success = await mutate(`/items/${id}`, 'PUT', { name, amount });
    if (success) setEditingId(null);
    return success;
  }
  async function deleteItem(id: string) {
    if (await mutate(`/items/${id}`, 'DELETE')) {
      if (editingId === id) setEditingId(null);
    }
  }
  async function undoDelete() {
    await mutate('/undo', 'POST');
  }
  async function toggle(itemId: string, personId: PersonId) {
    const selected = !assignments[itemId]?.includes(personId);
    await mutate(`/items/${itemId}/participants/${personId}`, 'PUT', { selected });
  }
  return <main className={s.page}>
    <header className={s.header}><a className={s.brand} href="./"><span className={s.logo}>÷</span> поровну</a><span className={s.demo}>Учебная версия · 04</span></header>
    <div className={s.intro}><span className={s.eyebrow}>Хороший вечер. Понятный счёт.</span><h1>Ужин с друзьями<span>Разделим?</span></h1><p>Выберите свои блюда. Общее разделим поровну<br className={s.desktopBreak} /> между теми, кто присоединился.</p></div>
    <div className={s.serverStatus}>
      <span role="status">{busy ? 'Сохраняем / загружаем…' : 'Чек загружен с сервера'}</span>
      <button disabled={busy} onClick={async () => { if (await perform()) setShowSummary(false); }}>Обновить чек</button>
      {error && <p role="alert">{error}</p>}
    </div>
    <fieldset className={`${s.layout} ${s.receiptFields}`} disabled={busy} aria-label="Работа с чеком"><section className={s.receipt} aria-label="Позиции чека">
      <div className={s.receiptHeader}><div><span className={s.eyebrow}>Чек № 001</span><h2>За нашим столом</h2></div><span className={s.table}>3 гостя</span></div>
      <fieldset className={s.switcher}><legend>Сейчас вы выбираете за</legend>
      <div>{participants.map(p => <button key={p.id} aria-pressed={active === p.id} className={active === p.id ? s.activePerson : s.person} onClick={() => setActive(p.id)}><span>{p.initial}</span>{p.name}</button>)}</div>
      </fieldset>
      <div className={s.list}>{items.map((item, index) => {
        const owners = assignments[item.id] ?? [];
        const selected = owners.includes(active);
        return <article key={item.id} className={s.item}><div className={s.itemTop}><span className={s.number}>{String(index + 1).padStart(2, '0')}</span><div className={s.itemName}><h3>{item.name}</h3><p>{item.detail}</p></div><strong>{money(item.amount)}</strong></div>
          <div className={s.itemBottom}><div className={s.ownerList}>{owners.length ? participants.filter(p => owners.includes(p.id)).map(p => <span key={p.id} className={s.owner}>{p.name}</span>) : <span className={s.muted}>Пока никто не выбрал</span>}{owners.length > 1 && <span className={s.muted}>поровну</span>}</div><button className={selected ? s.selected : s.choose} aria-pressed={selected} aria-label={`${selected ? 'Убрать' : 'Выбрать'}: ${item.name}, ${item.detail}, ${current.name}`} onClick={() => toggle(item.id, active)}>{selected ? '✓ Моё' : '+ Это моё'}</button></div>
          <div className={s.itemActions}>
            <button aria-label={`Редактировать: ${item.name}`} onClick={() => setEditingId(item.id)}>Редактировать</button>
            <button className={s.deleteButton} aria-label={`Удалить: ${item.name}`} onClick={() => deleteItem(item.id)}>Удалить</button>
          </div>
          {editingId === item.id && <AddItemForm key={item.id} initialItem={item} onAdd={(name, amount) => editItem(item.id, name, amount)} onCancel={() => setEditingId(null)} />}
        </article>;
      })}</div>
      {items.length === 0 && <div className={s.emptyState}><h3>В чеке пока пусто</h3><p>Добавьте первое блюдо с помощью формы ниже.</p></div>}
      {deleted && <div className={s.undoNotice}><div role="status">Удалено: {deleted.item.name}<small>Можно вернуть последнее удаление.</small></div><button onClick={undoDelete}>Отменить удаление</button></div>}
      <AddItemForm onAdd={addItem} />
      <div className={s.receiptTotal}><span>Итого по чеку</span><strong>{money(result.total)}</strong></div><p className={s.receiptNote}>Общее блюдо? Выберите его за каждого участника.</p>
    </section>
      <aside className={s.sidebar}><section className={s.summary} aria-label="Суммы участников"><span className={s.eyebrow}>Каждому — своё</span><h2>Счёт на компанию</h2><div className={s.progressLabel}><span>Распределено</span><strong>{result.total ? Math.round((result.total - result.unassigned) / result.total * 100) : 0}%</strong></div><progress max={result.total || 1} value={result.total - result.unassigned} aria-label="Распределённая сумма" />
        <div className={s.balances} aria-live="polite">{participants.map(p => <div className={s.balance} key={p.id}><span className={s.avatar}>{p.initial}</span><span>{p.name}{p.id === active && <small>вы выбираете</small>}</span><strong>{money(result.totals[p.id])}</strong></div>)}</div>
        <div className={s.remaining}><span>Осталось разделить</span><strong>{money(result.unassigned)}</strong></div>
        <button className={s.primary} disabled={items.length === 0 || result.unassigned > 0} onClick={() => setShowSummary(true)}>Посмотреть итог <span>↗</span></button>
        <p className={s.hint}>{items.length === 0 ? 'Добавьте первое блюдо в чек.' : result.unassigned ? 'Выберите участников для каждой позиции.' : 'Всё распределено. Можно подвести итог.'}</p>
        {showSummary && <div className={s.result} role="status"><strong>Чек разделён ✓</strong><p>{participants.map(p => `${p.name}: ${money(result.totals[p.id])}`).join(' · ')}</p><small>Это стоимость выбранных блюд. Учёт оплаты добавим на следующем этапе.</small></div>}
      </section><div className={s.note}><span>↳</span><p><strong>Пиццу можно на всех.</strong> Переключитесь на друга и выберите то же блюдо — сумма разделится автоматически.</p></div><button className={s.reset} onClick={() => void mutate('/reset', 'POST')}>Сбросить выбор</button></aside></fieldset>
    <footer className={s.footer}>Общий демочек · Данные сохраняются в PostgreSQL</footer>
  </main>;
}


