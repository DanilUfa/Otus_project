import React, { useEffect, useState } from 'react';
import { HubConnectionBuilder } from '@microsoft/signalr';
import { api, onSessionExpired, TYPE_LABELS, TYPE_VALUES, STATUS_LABELS } from './api';
import DayCalendar from './DayCalendar';

const MONTHS = [
  'Янв', 'Фев', 'Мар', 'Апр', 'Май', 'Июн',
  'Июл', 'Авг', 'Сен', 'Окт', 'Ноя', 'Дек'
];
const YEAR = new Date().getFullYear();
const pd = (s) => { const [y, m, d] = s.slice(0, 10).split('-').map(Number); return new Date(y, m - 1, d); };

export default function App() {
  const [user, setUser] = useState(() => JSON.parse(localStorage.getItem('user') || 'null'));
  const [error, setError] = useState('');
  const [, force] = useState(0);

  useEffect(() => { if (user) refresh(); }, [user]);

  // Если refresh на сервере отозван/истёк — сессия завершается принудительно.
  useEffect(() => {
    onSessionExpired(() => { setUser(null); sessionStorage.clear(); });
    return () => {}; // обработчики бывают не нужны после демонтажа — App живёт всё время
  }, []);

  // Realtime-подписка: новая заявка / смена статуса — мгновенно обновляем список и баланс.
  useEffect(() => {
    if (!user) return;
    const conn = new HubConnectionBuilder()
      .withUrl('/hubs/vacation', { accessTokenFactory: () => localStorage.getItem('token') || '' })
      .withAutomaticReconnect()
      .build();
    conn.on('LeaveCreated', refresh);
    conn.on('LeaveStatusChanged', refresh);
    conn.start().catch(() => { /* SignalR опционален */ });
    return () => { conn.stop().catch(() => {}); };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [user]);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  async function refresh() {
    try {
      const [leaves, users, balance] = await Promise.all([
        api.leaves(),
        api.users(),
        api.balance(user.userId, YEAR)
      ]);
      sessionStorage.setItem('leaves', JSON.stringify(leaves));
      sessionStorage.setItem('users', JSON.stringify(users));
      sessionStorage.setItem('balance', JSON.stringify(balance));
      // Read-модель (CQRS): календарь отдела берём из микросервиса reporting; best-effort.
      try {
        const rep = await api.reportCalendar(YEAR);
        sessionStorage.setItem('report', JSON.stringify(rep));
      } catch { sessionStorage.removeItem('report'); }
      force(x => x + 1);
    } catch (e) { setError(String(e.message)); }
  }

  if (!user) {
    return <Login onLogin={(u) => { setUser(u); }} />;
  }

  return (
    <div className="shell">
      <header>
        <h1>Планирование отпусков</h1>
        <div className="who">
          <b className={'role-' + user.role}>{user.name}</b>
          <span className="tag">{user.role === 'Manager' ? 'Руководитель' : 'Сотрудник'}</span>
          <button className="ghost" onClick={() => {
            // Отзываем refresh-токен на сервере (best-effort), потом чистим локально.
            api.logout().finally(() => { setUser(null); localStorage.clear(); sessionStorage.clear(); });
          }}>Выйти</button>
        </div>
      </header>

      {error && <div className="error">{error}</div>}

      <main>
        <section>
          <h2>Сводка</h2>
          <Data getter={sessionStorage} k="balance" render={(b) => (
            <div className="balance">Остаток годового отпуска: <b>{b.remaining}</b> дн. <small>({b.year})</small></div>
          )} />
        </section>

        <section>
          <h2>Подать заявку</h2>
          <SubmitForm userId={user.userId} onDone={() => { refresh(); }} />
        </section>

        <section>
          <h2>{user.role === 'Manager' ? 'Все заявки (согласование)' : 'Мои заявки'}</h2>
          <LeaveTable leaves={sessionStorage.getItem('leaves')} myUserId={user.userId}
            isManager={user.role === 'Manager'} onAction={() => refresh()} />
        </section>

        <YearCalendar />

        <DayCalendar />
      </main>
    </div>
  );
}

/* ----------  Разное ---------- */
function Data({ getter, k, render }) {
  const raw = getter.getItem(k);
  return raw ? render(JSON.parse(raw)) : <span className="dim">—</span>;
}

/* ----------  Вход ---------- */
// Демо-аккаунты для быстрого входа одним кликом (только для показа/ретро — пароли демо-сида).
const DEMO_ACCOUNTS = [
  { name: 'Иван Менеджер', role: 'Manager',   email: 'manager@vac.local', password: 'Manager123!' },
  { name: 'Пётр Сотрудник', role: 'Employee',  email: 'petr@vac.local', password: 'User123!' },
  { name: 'Мария Бухгалтер', role: 'Employee', email: 'maria@vac.local', password: 'User123!' },
  { name: 'Алексей Инженер', role: 'Employee', email: 'alex@vac.local', password: 'User123!' },
];

function Login({ onLogin }) {
  const [email, setEmail] = useState('petr@vac.local');
  const [password, setPassword] = useState('User123!');
  const [err, setErr] = useState('');
  const [busy, setBusy] = useState('');

  async function signIn(u, e) {
    e && e.preventDefault();
    try {
      const acc = await api.login(u.email, u.password);
      localStorage.setItem('token', acc.token);
      localStorage.setItem('refreshToken', acc.refreshToken);
      localStorage.setItem('user', JSON.stringify(acc));
      onLogin(acc);
    } catch (e2) { setErr(e2.message); setBusy(''); }
  }

  async function submit(e) {
    e.preventDefault();
    setErr('');
    await signIn({ email, password });
  }

  async function quick(acc) {
    setErr('');
    setBusy(acc.name);
    await signIn(acc);
  }

  return (
    <div className="login-wrap">
      <form className="card login" onSubmit={submit}>
        <h2>Вход в систему</h2>
        <label>Email
          <input value={email} onChange={(e) => setEmail(e.target.value)} />
        </label>
        <label>Пароль
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} />
        </label>
        {err && <div className="error">{err}</div>}
        <div className="row">
          <button disabled={!!busy}>Войти</button>
          <a href="/swagger" target="_blank" rel="noreferrer">Swagger API</a>
        </div>
      </form>

      <div className="card login quick">
        <h3>Быстрый вход (демо)</h3>
        <p className="dim">Один клик по аккаунту — пароль вводить не нужно.</p>
        {DEMO_ACCOUNTS.map((a) => (
          <button key={a.email} className={'acc role-' + (a.role === 'Manager' ? 'mgr' : 'emp')}
            disabled={!!busy} onClick={() => quick(a)}>
            <b>{a.name}</b>
            <span>{a.role === 'Manager' ? 'Руководитель' : 'Сотрудник'}</span>
            {busy === a.name && <em>Вход…</em>}
          </button>
        ))}
      </div>
    </div>
  );
}

/* ----------  Подача заявки ---------- */
function SubmitForm({ userId, onDone }) {
  const [type, setType] = useState(TYPE_VALUES.Annual);
  const [start, setStart] = useState(`${YEAR}-07-01`);
  const [end, setEnd] = useState(`${YEAR}-07-14`);
  const [reason, setReason] = useState('');
  const [file, setFile] = useState(null);
  const [msg, setMsg] = useState('');
  const [busy, setBusy] = useState(false);

  async function submit(e) {
    e.preventDefault();
    setMsg('');
    setBusy(true);
    try {
      const created = await api.createLeave(userId, Number(type), start, end, reason);
      if (file) {
        try {
          await api.uploadAttachment(created.id, file);
          setMsg('Заявка и документ отправлены на согласование.');
        } catch (e2) {
          setMsg('Заявка отправлена, но документ не прикрепился: ' + e2.message);
        }
        setFile(null);
      } else {
        setMsg('Заявка отправлена на согласование.');
      }
      onDone();
    } catch (e) { setMsg('Ошибка: ' + e.message); }
    setBusy(false);
  }

  async function saveDraft(e) {
    e.preventDefault();
    setMsg('');
    try {
      await api.saveDraft(userId, Number(type), start, end, reason);
      setMsg('Черновик сохранён — появится в списке со статусом «Черновик».');
      onDone();
    } catch (e) { setMsg('Ошибка: ' + e.message); }
  }

  return (
    <form className="card grid" onSubmit={submit}>
      <label>Тип
        <select value={type} onChange={(e) => setType(e.target.value)}>
          {Object.entries(TYPE_VALUES).map(([k, v]) => <option key={k} value={v}>{TYPE_LABELS[v]}</option>)}
        </select>
      </label>
      <label>Начало <input type="date" value={start} onChange={(e) => setStart(e.target.value)} /></label>
      <label>Конец <input type="date" value={end} onChange={(e) => setEnd(e.target.value)} /></label>
      <label>Причина <input value={reason} onChange={(e) => setReason(e.target.value)} /></label>
      <label>Документ (больничный/справка, до 10 МБ)
        <input type="file" accept=".pdf,.png,.jpg,.jpeg" onChange={(e) => setFile(e.target.files[0] || null)} />
      </label>
      <div className="row">
        <button disabled={busy}>Подать</button>
        <button className="ghost" type="button" onClick={saveDraft}>Сохранить черновик</button>
        {msg && <span className="ok">{msg}</span>}
      </div>
    </form>
  );
}

/* ----------  Таблица заявок ---------- */
function LeaveTable({ leaves, myUserId, isManager, onAction }) {
  if (!leaves) return <div className="dim">Загрузка…</div>;
  const list = JSON.parse(leaves);
  const shown = isManager ? list : list.filter((l) => l.userId === myUserId);
  if (!shown.length) return <div className="dim">Заявок нет.</div>;

  return (
    <table>
      <thead><tr><th>#</th><th>Сотрудник</th><th>Тип</th><th>Период</th><th>Дней</th><th>Статус</th><th>Документы</th><th></th></tr></thead>
      <tbody>
        {shown.map((l) => (
          <tr key={l.id}>
            <td>{l.id}</td>
            <td>{l.userName}</td>
            <td>{TYPE_LABELS[l.type]}</td>
            <td>{l.startDate.slice(0, 10)} … {l.endDate.slice(0, 10)}</td>
            <td>{l.days}</td>
            <td><span className={'st st-' + l.status}>{STATUS_LABELS[l.status]}</span></td>
            <td><AttachCell leaveId={l.id} canModify={isManager || myUserId === l.userId} /></td>
            {isManager && l.status === 1 && (
              <td className="actions">
                <button onClick={() => api.approve(l.id).then(onAction)}>Утвердить</button>
                <button className="warn" onClick={() => api.decline(l.id).then(onAction)}>Отклонить</button>
              </td>
            )}
            {(!isManager && myUserId === l.userId && l.status === 0) && (
              <td className="actions">
                <button onClick={() => api.submitDraft(l.id).then(onAction)}>Отправить</button>
                <button className="warn" onClick={() => api.cancelLeave(l.id).then(onAction)}>Отменить</button>
              </td>
            )}
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/* ----------  Документы заявки (приложения) ---------- */
function AttachCell({ leaveId, canModify }) {
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState(null);
  const [err, setErr] = useState('');

  async function toggle() {
    if (open) { setOpen(false); return; }
    setErr('');
    setItems(null);
    setOpen(true);
    try { setItems(await api.attachments(leaveId)); }
    catch (e) { setErr(String(e.message)); }
  }

  return (
    <span className="att">
      <button className="ghost sm att-btn" type="button" onClick={toggle}>📎{items && items.length > 0 && <span className="att-count">{items.length}</span>}</button>
      {open && (
        <div className="att-list card">
          {err && <div className="error">{err}</div>}
          {items === null ? <span className="dim">Загрузка…</span> :
            items.length === 0 ? <span className="dim">Нет документов</span> :
            items.map((a) => (
              <div key={a.id} className="att-item">
                <span className="att-name" title={a.contentType}>📄 {a.fileName} <small>{(a.length / 1024).toFixed(1)} КБ</small></span>
                <span className="att-actions">
                  <button className="ghost sm" onClick={() => api.downloadAttachment(a.id, a.fileName)}>Скачать</button>
                  {canModify && (
                    <button className="ghost sm warn" title="Удалить документ"
                      onClick={async () => { try { await api.deleteAttachment(a.id); toggle(); } catch (e) { setErr(String(e.message)); } }}>
                      ✕
                    </button>
                  )}
                </span>
              </div>
            ))}
        </div>
      )}
    </span>
  );
}

/* ----------  Годовой календарь-график ---------- */
function YearCalendar() {
  const raw = sessionStorage.getItem('leaves');
  const usersRaw = sessionStorage.getItem('users');
  const [year, setYear] = useState(new Date().getFullYear());
  if (!raw || !usersRaw) return <section><h2>Годовой график отпусков</h2><div className="dim">Нет данных для графика.</div></section>;

  const leaves = JSON.parse(raw);
  const users = JSON.parse(usersRaw);

  // попадает ли заявка в месяц m (1..12) именно выбранного года
  const inMonth = (l, y, m) => {
    const s = pd(l.startDate), e = pd(l.endDate);
    for (let d = new Date(s); d <= e; d.setDate(d.getDate() + 1)) {
      if (d.getFullYear() === y && d.getMonth() + 1 === m) return true;
    }
    return false;
  };
  // допускаются только согласованные (status 2) — как реальный график
  const approved = leaves.filter((l) => l.status === 2);
  const yearsAll = [year - 1, year, year + 1];

  return (
    <section>
      <h2>Годовой график отпусков · {year}</h2>
      <div className="y-legend">
        {yearsAll.map((y) => (
          <button key={y} className={'ghost sm' + (y === year ? ' on' : '')} onClick={() => setYear(y)}>{y}</button>
        ))}
      </div>
      <table className="cal">
        <thead><tr><th>Сотрудник</th>{MONTHS.map((m) => <th key={m}>{m}</th>)}</tr></thead>
        <tbody>
          {users.map((u) => (
            <tr key={u.id}>
              <td>{u.name}</td>
              {MONTHS.map((_, i) => {
                const hits = approved.filter((l) => l.userId === u.id && inMonth(l, year, i + 1));
                return (
                  <td key={i} className={hits.length ? 'cal-yes' : ''} title={hits.map((h) => `${h.startDate.slice(0, 10)}..${h.endDate.slice(0, 10)}`).join('\n')}>
                    {hits.length ? '▉' : ''}
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
      <p className="dim">Показаны только утверждённые отпуска. Наведите на ячейку — периоды.</p>
    </section>
  );
}

