import { useState } from 'react';
import { money, type PersonId } from './receipt';
import { useReceipt } from './useReceipt';
import { IssueInvite } from './Invitations';
import { AddItemForm } from './AddItemForm';
import s from './App.module.css';

export function ReceiptPage({ receiptId }: { receiptId: string }) {
  const { receipt, busy, error, perform } = useReceipt(receiptId);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [active, setActive] = useState<PersonId>('');
  const [adminMode, setAdminMode] = useState(false);
  const [invitePerson, setInvitePerson] = useState('');
  const [self, setSelf] = useState('');

  // Пока снимка нет, не показываем выдуманные нулевые суммы вместо данных сервера.
  if (!receipt) return <main className={s.page}><a href="#/">← Мои счета</a><h1>Поровну</h1>
    <p role="status">{busy ? 'Загружаем чек…' : 'Чек ещё не загружен.'}</p>
    {error && <p role="alert">{error}</p>}
    <button disabled={busy} onClick={() => void perform()}>Повторить загрузку</button>
  </main>;

  const { items, assignments, result, participants } = receipt;
  const owner = receipt.isOwner;
  const me = participants.find(p => p.id === receipt.myParticipantId);
  const editingLocked = receipt.isCompleted || Boolean(me?.finished);
  const adminAvailable = receipt.canSelectForOthers && !receipt.isCompleted;
  const admin = adminMode && adminAvailable;
  const current = admin ? participants.find(p => p.id === active) ?? me ?? participants[0] : me;
  const recipient = participants.find(p => p.id === invitePerson);
  const selectionLocked = receipt.isCompleted || Boolean(current?.finished);
  const confirmed = participants.filter(p => p.finished).length;
  const lastWithRemainder = current && !current.finished && confirmed === participants.length - 1 && result.unassigned > 0;
  const mutate = perform;
  function confirmSelection() {
    if (!current) return;
    const action = current.finished ? 'reopen' : 'finish';
    const prefix = admin ? '/admin/participants/' + encodeURIComponent(current.id) : '';
    return mutate(prefix + '/' + action, 'POST');
  }
  async function addItem(name: string, amount: number, quantity: number, byPortion: boolean) {
    return mutate('/items', 'POST', { name, amount, quantity, byPortion });
  }
  async function editItem(id: string, name: string, amount: number, quantity: number, byPortion: boolean) {
    const success = await mutate(`/items/${id}`, 'PUT', { name, amount, quantity, byPortion });
    if (success) setEditingId(null);
    return success;
  }
  async function deleteItem(id: string) {
    if (await mutate(`/items/${id}`, 'DELETE')) {
      if (editingId === id) setEditingId(null);
    }
  }
  async function toggle(itemId: string, personId: PersonId, count?: number) {
    const selected = count === undefined ? !assignments[itemId]?.includes(personId) : count > 0;
    await mutate(`/items/${itemId}/participants/${personId}`, 'PUT', { selected, count, adminMode: admin });
  }
  return <main className={s.page}>
    <header className={s.header}><a className={s.brand} href="#/"><span className={s.logo}>÷</span> поровну</a><span className={s.demo}>Мои счета</span></header>
    <a className={s.backLink} href="#/">← Все счета</a><div className={s.intro}><span className={s.eyebrow}>Хороший вечер. Понятный счёт.</span><h1>{receipt.title}<span>Разделим?</span></h1><p>Выберите свои блюда. Общее разделим поровну<br className={s.desktopBreak} /> между теми, кто присоединился.</p></div>
    <div className={s.serverStatus}>
      <span role="status">{busy ? 'Сохраняем / загружаем…' : 'Чек загружен с сервера'}</span>
      <button disabled={busy} onClick={() => void perform()}>Обновить чек</button>
      {error && <p role="alert">{error}</p>}
    </div>
    <fieldset className={`${s.layout} ${s.receiptFields}`} disabled={busy} aria-label="Работа с чеком"><section className={s.receipt} aria-label="Позиции чека">
      <div className={s.receiptHeader}><div><span className={s.eyebrow}>Ваш счёт</span><h2>За нашим столом</h2></div><span className={s.table}>Участников: {participants.length}</span></div>
      {owner && !me && !editingLocked && <fieldset className={s.identity}><legend>Кто вы в этом счёте?</legend>
        <select aria-label="Ваше имя" value={self} onChange={e => setSelf(e.target.value)}>
          <option value="">Выберите своё имя</option>{participants.filter(p => !p.joined && !p.finished).map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
        </select><button className={s.addButton} disabled={!self} onClick={() => mutate('/me/' + self, 'PUT')}>Это я</button>
        <small>Имя закрепится за вашим аккаунтом в этом счёте.</small></fieldset>}
      {adminAvailable && <button className={s.adminToggle} aria-pressed={admin} onClick={() => setAdminMode(!admin)}>
        {admin ? 'Выйти из режима администратора' : 'Режим администратора'}</button>}
      {admin ? <fieldset className={s.switcher}><legend>Выбор за участника</legend>
      <div>{participants.map(p => <button key={p.id} aria-pressed={current?.id === p.id} className={current?.id === p.id ? s.activePerson : s.person} onClick={() => setActive(p.id)}><span>{p.initial}</span>{p.name}</button>)}</div>
      </fieldset> : me && <p>Вы выбираете за себя · {me.name}</p>}
      {owner && !receipt.isCompleted && <details className={s.invitation}><summary>Пригласить участника</summary>
        <select aria-label="Кого пригласить" value={invitePerson} onChange={e => setInvitePerson(e.target.value)}>
          <option value="">Выберите участника</option>{participants.filter(p => !p.joined && !p.finished).map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
        </select>{recipient && <IssueInvite key={recipient.id} person={recipient} receiptId={receiptId} />}</details>}
      <div className={s.list}>{items.map((item, index) => {
        const owners = assignments[item.id] ?? [];
        const selected = current && owners.includes(current.id);
        const taken = Object.values(item.counts ?? {}).reduce((sum, n) => sum + n, 0);
        const mine = current ? item.counts?.[current.id] ?? 0 : 0;
        return <article key={item.id} className={s.item}><div className={s.itemTop}><span className={s.number}>{String(index + 1).padStart(2, '0')}</span><div className={s.itemName}><h3>{item.name}</h3><p>{item.quantity} × {money(item.amount / item.quantity)} · {item.byPortion ? `свободно: ${item.quantity - taken}` : 'поровну'}</p></div><strong>{money(item.amount)}</strong></div>
          <div className={s.itemBottom}><div className={s.ownerList}>{owners.length ? participants.filter(p => owners.includes(p.id)).map(p => <span key={p.id} className={s.owner}>{p.name}{item.byPortion && ` · ${item.counts?.[p.id]} шт.`}</span>) : <span className={s.muted}>Пока никто не выбрал</span>}{!item.byPortion && owners.length > 1 && <span className={s.muted}>поровну</span>}</div>{current && item.byPortion && <div className={s.portionButtons}>
            <button disabled={selectionLocked || mine === 0} aria-label={`Вернуть порцию: ${item.name}`} onClick={() => toggle(item.id, current.id, mine - 1)}>−</button>
            <span aria-live="polite">{mine} шт.</span>
            <button disabled={selectionLocked || taken >= item.quantity} aria-label={`Взять порцию: ${item.name}`} onClick={() => toggle(item.id, current.id, mine + 1)}>+</button>
          </div>}{current && !item.byPortion && <button disabled={selectionLocked} className={selected ? s.selected : s.choose} aria-pressed={selected} aria-label={`${selected ? 'Убрать' : 'Выбрать'}: ${item.name}, ${item.detail}, ${current.name}`} onClick={() => toggle(item.id, current.id)}>{selected ? (admin ? 'Убрать выбор' : '✓ Моё') : (admin ? 'Выбрать' : '+ Это моё')}</button>}</div>
          {owner && !editingLocked && <div className={s.itemActions}>
            <button aria-label={`Редактировать: ${item.name}`} onClick={() => setEditingId(item.id)}>Редактировать</button>
            <button className={s.deleteButton} aria-label={`Удалить: ${item.name}`} onClick={() => deleteItem(item.id)}>Удалить</button>
          </div>}
          {owner && !editingLocked && editingId === item.id && <AddItemForm key={item.id} initialItem={item} onAdd={(...values) => editItem(item.id, ...values)} onCancel={() => setEditingId(null)} />}
        </article>;
      })}</div>
      {items.length === 0 && <div className={s.emptyState}><h3>В чеке пока пусто</h3><p>{owner ? 'Добавьте первое блюдо с помощью формы ниже.' : 'Владелец пока не добавил блюда.'}</p></div>}
      {owner && !editingLocked && <AddItemForm onAdd={addItem} />}
      <div className={s.receiptTotal}><span>Итого по чеку</span><strong>{money(result.total)}</strong></div><p className={s.receiptNote}>В общей строке стоимость делится поровну. В строке по порциям каждый оплачивает своё количество.</p>
    </section>
      <aside className={s.sidebar}><section className={s.summary} aria-label="Суммы участников"><span className={s.eyebrow}>Каждому — своё</span><h2>Счёт на компанию</h2><div className={s.progressLabel}><span>Распределено</span><strong>{result.total ? Math.round((result.total - result.unassigned) / result.total * 100) : 0}%</strong></div><progress max={result.total || 1} value={result.total - result.unassigned} aria-label="Распределённая сумма" />
        <div className={s.balances} aria-live="polite">{participants.map(p => <div className={s.balance} key={p.id}><span className={s.avatar}>{p.initial}</span><span>{p.name}{p.finished && <small>Подтверждено ✓</small>}{p.id === current?.id && <small>вы выбираете</small>}</span><strong>{money(result.totals[p.id])}</strong></div>)}</div>
        <div className={s.remaining}><span>Осталось разделить</span><strong>{money(result.unassigned)}</strong></div>
        {receipt.isCompleted ? <p className={s.result} role="status"><strong>Счёт завершён ✓</strong></p> :
          current && <button className={s.primary} disabled={busy || (!current.finished && items.length === 0) || Boolean(lastWithRemainder)}
            onClick={confirmSelection}>
            {admin ? (current.finished ? 'Изменить выбор' : 'Подтвердить') + ' · ' + current.name
              : current.finished ? 'Изменить чек' : 'Я закончил'}
          </button>}
        {admin && <button className={s.primary} disabled={busy || items.length === 0 || result.unassigned > 0}
          onClick={() => mutate('/admin/finish-all', 'POST')}>Завершить за всех</button>}
        <p className={s.hint}>Подтвердили {confirmed} из {participants.length}. {receipt.isCompleted
          ? 'Личные итоги переданы боту для отправки.'
          : lastWithRemainder ? 'Для последнего подтверждения распределите остаток.'
          : admin ? 'За кого вы подтвердите выбор, по тем участникам бот пришлёт расчёт вам.'
          : me?.finished ? 'Пока счёт открыт, вы можете вернуться к выбору.'
          : 'Когда все подтвердят выбор и распределят всю сумму, бот отправит личные итоги.'}</p>
      </section>{owner && admin && !editingLocked && <button className={s.reset} onClick={() => void mutate('/reset', 'POST')}>Сбросить общий выбор</button>}</aside></fieldset>
    <footer className={s.footer}>У каждого счёта — свои позиции и участники</footer>
  </main>;
}




