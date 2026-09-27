import React, { useState } from 'react';

const MONTH_NAMES = ['Январь','Февраль','Март','Апрель','Май','Июнь','Июль','Август','Сентябрь','Октябрь','Ноябрь','Декабрь'];
const WEEKSHORT = ['Вс','Пн','Вт','Ср','Чт','Пт','Сб'];

const pd = (s) => { const [y, m, d] = s.slice(0, 10).split('-').map(Number); return new Date(y, m - 1, d); };
const fmt = (d) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const daysInMonth = (y, m) => new Date(y, m + 1, 0).getDate();

/**
 * Календарь отдела по ДНЯМ: строки — выбранные сотрудники, колонки — дни месяца.
 * Отпуск каждого — непрерывный выделенный промежуток; дни с пересечением отпусков
 * (двое/более сотрудников) подсвечиваются отдельно.
 */
export default function DayCalendar() {
  const raw = sessionStorage.getItem('leaves');
  const usersRaw = sessionStorage.getItem('users');
  const [ym, setYm] = useState(() => {
    const n = new Date(); let y = n.getFullYear(), m = n.getMonth();
    try {
      const lv = JSON.parse(sessionStorage.getItem('leaves') || '[]');
      const ap = lv.filter((l) => l.status === 2);
      // месяц с максимальным числом утверждённых отпусков
      if (ap.length) {
        const cnt = {};
        ap.forEach((l) => { const d = pd(l.startDate); const key = `${d.getFullYear()}-${d.getMonth()}`; cnt[key] = (cnt[key] || 0) + 1; });
        const best = Object.keys(cnt).sort((a, b) => cnt[b] - cnt[a])[0];
        y = +best.split('-')[0]; m = +best.split('-')[1];
      }
    } catch {}
    return { y, m };
  });
  const [sel, setSel] = useState([]);
  const [withPending, setWithPending] = useState(false);

  if (!raw || !usersRaw) return <section><h2>Календарь отдела (по дням)</h2><div className="dim">Нет данных.</div></section>;

  const users = JSON.parse(usersRaw);
  const leaves = JSON.parse(raw);
  const showStatuses = withPending ? [2, 1] : [2];
  const shownLeaves = leaves.filter((l) => showStatuses.includes(l.status));
  const name = (id) => (users.find((u) => u.id === id) || {}).name || `User ${id}`;
  const allUserIds = shownLeaves.map((l) => l.userId).filter((v, i, a) => a.indexOf(v) === i);
  const shown = sel.length ? sel : allUserIds;

  const { y, m } = ym;
  const dim = daysInMonth(y, m);
  const monthFrom = new Date(y, m, 1), monthTo = new Date(y, m, dim);

  // По дням: какие сотрудники в отпуске (и справочник день -> заявка)
  const empLeave = {};   // uid -> Map(day -> leave)
  const dayEmp = {};     // day -> Set(uid)  (для пересечений)

  shownLeaves.forEach((l) => {
    if (!shown.includes(l.userId)) return;
    const s = pd(l.startDate), e = pd(l.endDate);
    const lo = Math.max(+monthFrom, +s), hi = Math.min(+monthTo, +e);
    if (lo > hi) return; // заявка вне текущего месяца
    for (let d = new Date(lo); d.getTime() <= hi; d = new Date(y, m, d.getDate() + 1)) {
      const day = d.getDate();
      (empLeave[l.userId] = empLeave[l.userId] || new Map()).set(day, l);
      (dayEmp[day] = dayEmp[day] || new Set()).add(l.userId);
    }
  });

  const days = Array.from({ length: dim }, (_, i) => i + 1);
  const toggle = (id) => setSel((prev) => {
    const base = prev.length ? prev : allUserIds; // при «показать всех» клик по активному = убрать его
    return base.includes(id) ? base.filter((x) => x !== id) : [...base, id];
  });
  const nav = (dy) => { const d = new Date(y, m + dy, 1); setYm({ y: d.getFullYear(), m: d.getMonth() }); };

  // сводка по пересечениям на видимый месяц
  const overlapDays = days.filter((d) => (dayEmp[d] || new Set()).size > 1);
  const overlapSummary = overlapDays.map((d) => {
    const who = [...(dayEmp[d] || new Set())].map((id) => name(id)).join(', ');
    return `${String(d).padStart(2, '0')}.${String(m + 1).padStart(2, '0')} — ${who}`;
  });

  return (
    <section className="day-cal">
      <h2>Календарь отдела по дням · {y}</h2>

      <div className="legend">
        <button type="button" onClick={() => nav(-1)}>‹</button>
        <b>{MONTH_NAMES[m]} {y}</b>
        <button type="button" onClick={() => nav(1)}>›</button>
        <button type="button" onClick={() => setSel([])}>Все</button>
        &nbsp;
        {users.map((u) => (
          <label key={u.id}>
            <input type="checkbox" checked={shown.includes(u.id)} onChange={() => toggle(u.id)} />
            {u.name}
          </label>
        ))}
        <label className="dim">
          <input type="checkbox" checked={withPending} onChange={(e) => setWithPending(e.target.checked)} />
          вкл. «на рассмотрении»
        </label>
      </div>

      {(!shownLeaves.length || !shown.length)
        ? <div className="dim">Нет отпусков для отображения.</div>
        : (
          <div className="day-scroll">
            <table className="cal day">
              <thead>
                <tr>
                  <th>Сотрудник</th>
                  {days.map((d) => {
                    const wd = new Date(y, m, d).getDay();
                    return <th key={d} className={wd === 0 || wd === 6 ? 'd-weh' : ''} title={WEEKSHORT[wd]}>{d}</th>;
                  })}
                </tr>
              </thead>
              <tbody>
                {shown.map((uid) => {
                  const mine = empLeave[uid] || new Map();
                  return (
                    <tr key={uid}>
                      <td className="d-name">{name(uid)}</td>
                      {days.map((d) => {
                        const l = mine.get(d);
                        if (!l) {
                          const wd = new Date(y, m, d).getDay();
                          return <td key={d} className={wd === 0 || wd === 6 ? 'd-off d-we' : 'd-off'}></td>;
                        }
                        const overlapping = (dayEmp[d] || new Set()).size > 1;
                        const t = `${name(uid)}: ${fmt(pd(l.startDate))}..${fmt(pd(l.endDate))}`;
                        return <td key={d} className={overlapping ? 'd-on d-over' : (l.id % 2 ? 'd-on d-a' : 'd-on d-b')} title={t}></td>;
                      })}
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}

      {overlapDays.length > 0 && (
        <div className="overlaps">
          <b>Пересечения отпусков в {MONTH_NAMES[m]} ({overlapDays.length} дн.):</b>
          {overlapSummary.length <= 15
            ? <ul>{overlapSummary.map((s, i) => <li key={i}>{s}</li>)}</ul>
            : <p className="dim">Дней с пересечением: {overlapDays.length} — наведите на красную ячейку.</p>}
        </div>
      )}
    </section>
  );
}