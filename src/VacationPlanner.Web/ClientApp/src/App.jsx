import React, { useEffect, useState } from 'react';
import { HubConnectionBuilder } from '@microsoft/signalr';
import { api, TYPE_LABELS, TYPE_VALUES, STATUS_LABELS } from './api';

const MONTHS = [
  'Янв', 'Фев', 'Мар', 'Апр', 'Май', 'Июн',
  'Июл', 'Авг', 'Сен', 'Окт', 'Ноя', 'Дек'
];
const YEAR = new Date().getFullYear();

export default function App() {
  const [user, setUser] = useState(() => JSON.parse(localStorage.getItem('user') || 'null'));
  const [error, setError] = useState('');
  const [, force] = useState(0);

  useEffect(() => { if (user) refresh(); }, [user]);

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
          <button className="ghost" onClick={() => { setUser(null); localStorage.clear(); }}>Выйти</button>
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
function Login({ onLogin }) {
  const [email, setEmail] = useState('petr@vac.local');
  const [password, setPassword] = useState('User123!');
  const [err, setErr] = useState('');

  async function submit(e) {
    e.preventDefault();
    setErr('');
    try {
      const u = await api.login(email, password);
      localStorage.setItem('token', u.token);
      localStorage.setItem('user', JSON.stringify(u));
      onLogin(u);
    } catch (e) { setErr(e.message); }
  }

  return (
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
        <button>Войти</button>
        <a href="/swagger" target="_blank" rel="noreferrer">Swagger API</a>
      </div>
    </form>
  );
}

/* ----------  Подача заявки ---------- */
function SubmitForm({ userId, onDone }) {
  const [type, setType] = useState(TYPE_VALUES.Annual);
  const [start, setStart] = useState(`${YEAR}-07-01`);
  const [end, setEnd] = useState(`${YEAR}-07-14`);
  const [reason, setReason] = useState('');
  const [msg, setMsg] = useState('');

  async function submit(e) {
    e.preventDefault();
    setMsg('');
    try {
      await api.createLeave(userId, Number(type), start, end, reason);
      setMsg('Заявка отправлена на согласование.');
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
      <div>
        <button>Подать</button>
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
      <thead><tr><th>#</th><th>Сотрудник</th><th>Тип</th><th>Период</th><th>Дней</th><th>Статус</th>{isManager && <th></th>}</tr></thead>
      <tbody>
        {shown.map((l) => (
          <tr key={l.id}>
            <td>{l.id}</td>
            <td>{l.userName}</td>
            <td>{TYPE_LABELS[l.type]}</td>
            <td>{l.startDate.slice(0, 10)} … {l.endDate.slice(0, 10)}</td>
            <td>{l.days}</td>
            <td><span className={'st st-' + l.status}>{STATUS_LABELS[l.status]}</span></td>
            {isManager && l.status === 1 && (
              <td className="actions">
                <button onClick={() => api.approve(l.id).then(onAction)}>Утвердить</button>
                <button className="warn" onClick={() => api.decline(l.id).then(onAction)}>Отклонить</button>
              </td>
            )}
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/* ----------  Годовой календарь-график ---------- */
function YearCalendar() {
  const raw = sessionStorage.getItem('leaves');
  const usersRaw = sessionStorage.getItem('users');
  if (!raw || !usersRaw) return <section><h2>Годовой график отпусков</h2><div className="dim">Нет данных для графика.</div></section>;

  const leaves = JSON.parse(raw);
  const users = JSON.parse(usersRaw);

  // попадает ли заявка в месяц m (1..12)
  const inMonth = (l, m) => {
    const s = new Date(l.startDate), e = new Date(l.endDate);
    for (let d = new Date(s); d <= e; d.setMonth(d.getMonth() + 1)) {
      if (d.getMonth() + 1 === m) return true;
    }
    return false;
  };
  // допускаются только согласованные (status 2) — как реальный график
  const approved = leaves.filter((l) => l.status === 2);

  return (
    <section>
      <h2>Годовой график отпусков · {YEAR}</h2>
      <table className="cal">
        <thead><tr><th>Сотрудник</th>{MONTHS.map((m) => <th key={m}>{m}</th>)}</tr></thead>
        <tbody>
          {users.map((u) => (
            <tr key={u.id}>
              <td>{u.name}</td>
              {MONTHS.map((_, i) => {
                const hits = approved.filter((l) => l.userId === u.id && inMonth(l, i + 1));
                return (
                  <td key={i} className={hits.length ? 'cal-yes' : ''} title={hits.map((h) => `${h.startDate.slice(5, 10)}..${h.endDate.slice(5, 10)}`).join('\n')}>
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