import { useId, useRef, useState, type FormEvent } from 'react';
import type { Item } from './receipt';
import { parsePrice } from './price';
import s from './App.module.css';

type Props = {
  onAdd: (name: string, amount: number) => Promise<boolean>;
  initialItem?: Item;
  onCancel?: () => void;
};
// Компонент формы добавления/редактирования позиции. Валидация на клиенте, но окончательная проверка на сервере.
export function AddItemForm({ onAdd, initialItem, onCancel }: Props) {
  const prefix = useId();
  const [name, setName] = useState(initialItem?.name ?? '');
  const [price, setPrice] = useState(initialItem ? (initialItem.amount / 100).toFixed(2).replace('.', ',') : '');
  const [errors, setErrors] = useState<{ name?: string; price?: string }>({});
  const [message, setMessage] = useState('');
  const nameInput = useRef<HTMLInputElement>(null);
  const priceInput = useRef<HTMLInputElement>(null);
  
  // Функция обработки отправки формы. Проверяет поля, вызывает onAdd и очищает черновик только после успешного сохранения на сервере.
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const trimmedName = name.trim();
    const amount = parsePrice(price);
    const nextErrors = {
      name: !trimmedName ? 'Введите название блюда.' : undefined,
      price: amount === null ? 'Введите цену от 0,01 до 1 000 000 ₽, не более двух знаков после запятой.' : undefined,
    };
    setErrors(nextErrors);
    setMessage('');
    if (nextErrors.name || nextErrors.price) {
      (nextErrors.name ? nameInput : priceInput).current?.focus();
      return;
    }
    // Очищаем черновик только после подтверждения сервера. При ошибке можно исправить или повторить ввод.
    const saved = await onAdd(trimmedName, amount!);
    if (!saved) return;
    if (initialItem) return;
    setName('');
    setPrice('');
    setMessage(`Добавлено: ${trimmedName}. Теперь выберите участников для этой позиции.`);
    nameInput.current?.focus();
  }

  return <form className={s.addForm} onSubmit={submit} noValidate aria-labelledby={`${prefix}-title`}>
    <h3 id={`${prefix}-title`}>{initialItem ? 'Редактировать позицию' : 'Добавить в чек'}</h3>
    <p>Одна позиция или порция. Её можно разделить между друзьями.</p>
    <div className={s.formFields}>
      <div><label htmlFor={`${prefix}-item-name`}>Название блюда</label>
        <input ref={nameInput} autoFocus={!!initialItem} id={`${prefix}-item-name`} value={name} maxLength={100} placeholder="Например, чизкейк" aria-invalid={!!errors.name} aria-describedby={errors.name ? `${prefix}-name-error` : undefined} onChange={e => { setName(e.target.value); setErrors(old => ({ ...old, name: undefined })); setMessage(''); }} />
        {errors.name && <span id={`${prefix}-name-error`} className={s.error}>{errors.name}</span>}
      </div>
      <div><label htmlFor={`${prefix}-item-price`}>Цена, ₽</label>
        <input ref={priceInput} id={`${prefix}-item-price`} value={price} inputMode="decimal" maxLength={16} placeholder="350,00" aria-invalid={!!errors.price} aria-describedby={errors.price ? `${prefix}-price-error` : undefined} onChange={e => { setPrice(e.target.value); setErrors(old => ({ ...old, price: undefined })); setMessage(''); }} />
        {errors.price && <span id={`${prefix}-price-error`} className={s.error}>{errors.price}</span>}
      </div>
    </div>
    <button className={s.addButton} type="submit">{initialItem ? 'Сохранить изменения' : '+ Добавить позицию'}</button>{onCancel && <button type="button" className={s.cancelEdit} onClick={onCancel}>Отмена</button>}
    <div className={s.formMessage} role="status">{message}</div>
  </form>;
}


