import { useEffect, useState } from 'react';
import { ReceiptPage } from './ReceiptPage';
import { ReceiptsPage } from './ReceiptsPage';

// Hash хранит выбранный счёт в URL. Для двух экранов достаточно браузерной
// навигации: работают прямые ссылки, перезагрузка и кнопки «Назад»/«Вперёд».
export function App() {
  const [hash, setHash] = useState(window.location.hash);
  useEffect(() => {
    const navigate = () => { setHash(window.location.hash); window.scrollTo(0, 0); };
    window.addEventListener('hashchange', navigate);
    return () => window.removeEventListener('hashchange', navigate);
  }, []);
  if (!hash || hash === '#' || hash === '#/') return <ReceiptsPage />;
  const match = /^#\/receipts\/([a-zA-Z0-9-]+)$/.exec(hash);
  // key создаёт новое состояние для нового счёта: выбор и формы не «переезжают» между счетами.
  if (match) return <ReceiptPage key={match[1]} receiptId={match[1]} />;
  return <main style={{padding:32}}><h1>Страница не найдена</h1><a href="#/">Вернуться к счетам</a></main>;
}
