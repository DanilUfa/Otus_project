import React, { useState, useEffect } from 'react';
import { api } from './api';

const MONTHS = ['Январь','Февраль','Март','Апрель','Май','Июнь','Июль','Август','Сентябрь','Октябрь','Ноябрь','Декабрь'];
const WD = ['Пн','Вт','Ср','Чт','Пт','Сб','Вс'];
const CURRENT_YEAR = new Date().getFullYear();

const isLeap = (y) => (y % 4 === 0 && y % 100 !== 0) || y % 400 === 0;
const DAYS_OF = (y) => (isLeap(y) ? 366 : 365);
const pd = (s) => { const [y, m, d] = s.slice(0, 10).split('-').map(Number); return new Date(y, m - 1, d); };
const dateOf = (year, i) => new Date(year, 0, 1 + i);
const fmt = (d) => String(d.getDate()).padStart(2, '0') + '.' + String(d.getMonth() + 1).padStart(2, '0');
const monOffset = (year, m) => (new Date(year, m, 1).getDay() + 6) % 7; // 0 = Пн

/**
 * Развёрнутый ГОДОВОЙ календарь отпусков (с переключателем года):
 * 12 мини-календарей (реальные недели) выбранного года. Данные — read-модель reporting
 * (CQRS, best-effort); при смене года запрашивается /api/reports/calendar?year=Y.
 * Отмеченные сотрудники (чекбоксы) показывают свои отпуска; пересечение >=2 — красная рамка.
 */
export default function AnnualCalendar() {
  const [year, setYear] = useState(CURRENT_YEAR);
  const [report, setReport] = useState(() => {
    try { return JSON.parse(sessionStorage.getItem('report')); } catch { return null; }
  });
  const raw = report && report.leaves ? JSON.stringify(report.leaves) : sessionStorage.getItem('leaves');
  const usersRaw = sessionStorage.getItem('users');
  const [sel, setSel] = useState(null);   // null = все, [] = никого, иначе массив id
  const [withPending, setWithPending] = useState(false);
  const [holidays, setHolidays] = useState({});   // 'YYYY-MM-DD' -> название

  // Смена года: подтягиваем read-модель этого года (best-effort: кэш остаётся) и праздники.
  useEffect(() => {
    api.reportCalendar(year)
      .then((rep) => { setReport(rep); sessionStorage.setItem('report', JSON.stringify(rep)); })
      .catch(() => setReport(null));
    api.holidays(year)
      .then((list) => {
        const m = {};
        (list || []).forEach((h) => {
          const d = pd(h.date);
          m[d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0')] = { n: h.name, k: h.kind || 0 };
        });
        setHolidays(m);
      })
      .catch(() => setHolidays({}));
  }, [year]);

  if (!raw || !usersRaw) {
    return <section className="year-cal"><h2>Годовой календарь отпусков · {year}</h2><div className="dim">Нет данных.</div></section>;
  }

  const users = JSON.parse(usersRaw);
  const leaves = JSON.parse(raw);
  const statuses = withPending ? [1, 2] : [2];
  const vis = leaves.filter((l) => statuses.includes(l.status));
  const name = (id) => (users.find((u) => u.id === id) || {}).name || ('User ' + id);
  const allUserIds = vis.map((l) => l.userId).filter((v, i, a) => a.indexOf(v) === i);
  const shown = sel === null ? allUserIds : sel;
  const palette = ['#2563eb', '#0d9488', '#7c3aed', '#d97706', '#db2777', '#0891b2'];
  const ultOf = (id) => palette[users.findIndex((u) => u.id === id) % palette.length];
  const shownVis = vis.filter((l) => shown.includes(l.userId));

  const DAYS = DAYS_OF(year);
  const y0 = new Date(year, 0, 1);
  const dim = (m) => new Date(year, m + 1, 0).getDate();
  const idx = (d) => Math.floor((d - y0) / 86400000);

  const dayUids = Array.from({ length: DAYS }, () => new Set());
  const dayStatus = Array.from({ length: DAYS }, () => ({}));
  shownVis.forEach((l) => {
    const a = Math.max(0, idx(pd(l.startDate)));
    const b = Math.min(DAYS - 1, idx(pd(l.endDate)));
    if (a > b) return; // заявка вне выбранного года
    for (let i = a; i <= b; i++) {
      dayUids[i].add(l.userId);
      dayStatus[i][l.userId] = dayStatus[i][l.userId] || new Set();
      dayStatus[i][l.userId].add(l.status);
    }
  });

  const runs = {};
  shown.forEach((uid) => {
    const list = [];
    for (let i = 0; i < DAYS; i++) if (dayUids[i].has(uid)) list.push(i);
    const r = []; let start = null, prev = null;
    list.forEach((d) => {
      if (start === null) start = d;
      else if (d !== prev + 1) { r.push([start, prev]); start = d; }
      prev = d;
    });
    if (start !== null) r.push([start, prev]);
    runs[uid] = r;
  });
  const runOf = (uid, i) => (runs[uid] || []).find(([a, b]) => i >= a && i <= b);

  const onLeavers = (i) => shown.filter((u) => dayUids[i].has(u));
  const dayTip = (i) => {
    const ls = onLeavers(i);
    if (!ls.length) return '';
    return ls.map((u) => {
      const ru = runOf(u, i);
      const pend = ![...(dayStatus[i][u] || [])].some((s) => s === 2);
      return name(u) + ': ' + (ru ? fmt(dateOf(year, ru[0])) + '..' + fmt(dateOf(year, ru[1])) : '') + (pend ? ' (на рассм.)' : '');
    }).join('; ');
  };

  const toggle = (uid) => {
    const base = sel === null ? allUserIds : sel;
    const next = base.includes(uid) ? base.filter((x) => x !== uid) : [...base, uid];
    setSel(next);
  };

  const monthsData = MONTHS.map((mName, m) => {
    const offset = monOffset(year, m);
    const cells = [];
    for (let k = 0; k < offset; k++) cells.push(null);
    for (let d = 1; d <= dim(m); d++) cells.push({ d, i: idx(new Date(year, m, d)) });
    const hud = new Set();
    for (let d = 1; d <= dim(m); d++) if (dayUids[idx(new Date(year, m, d))].size) hud.add(d);
    return { m, name: mName, cells, vacationDays: hud.size };
  });

  const years = [year - 1, year, year + 1];

  return (
    <section className="year-cal">
      <h2>Годовой календарь отпусков · {year}</h2>

      <div className="y-legend">
        <span className="lg"><span className="sw app" />Утверждён</span>
        <span className="lg"><span className="sw pend" />На рассмотрении</span>
        <span className="lg"><span className="sw over" />Пересечение (&ge;2)</span>
        <span className="lg"><span className="sw dot" />Точка = день сотрудника</span>
        <span className="lg"><span className="sw hol" />Праздник</span>
        <span className="lg"><span className="sw shift" />Перенос выходного</span>
        <span className="lg"><span className="sw short" />Сокращённый день</span>
        <span className="sp" />
        {years.map((y) => (
          <button key={y} className={'ghost sm' + (y === year ? ' on' : '')} onClick={() => setYear(y)}>{y}</button>
        ))}
        <button className="ghost sm" onClick={() => setSel(null)}>Все</button>
        <button className="ghost sm" onClick={() => setSel([])}>Никого</button>
        <label className="tog"><input type="checkbox" checked={withPending} onChange={(e) => setWithPending(e.target.checked)} /> вкл. «на рассмотрении»</label>
      </div>

      <div className="y-pick">
        {users.map((u) => (
          <label key={u.id} className="chk">
            <input type="checkbox" checked={shown.includes(u.id)} onChange={() => toggle(u.id)} />
            <i className="dot" style={{ background: ultOf(u.id) }} />
            {u.name}
          </label>
        ))}
      </div>

      {shown.length === 0 ? (
        <div className="dim">Выберите сотрудников, чьи отпуска показать.</div>
      ) : (
        <div className="months">
          {monthsData.map((mo) => (
            <div className="mon" key={mo.name + year}>
              <div className="mon-head">
                <b>{mo.name}</b>
                {mo.vacationDays > 0 && <span>{mo.vacationDays} дн.</span>}
              </div>
              <div className="wd">
                {WD.map((w, k) => <span key={w} className={k >= 5 ? 'we' : ''}>{w}</span>)}
              </div>
              <div className="days">
                {mo.cells.map((c, k) => {
                  if (!c) return <div className="blank" key={k} />;
                  const ls = onLeavers(c.i);
                  const over = ls.length > 1;
                  const we = (new Date(year, mo.m, c.d).getDay() + 6) % 7 >= 5;
                  const hd = holidays[year + '-' + String(mo.m + 1).padStart(2, '0') + '-' + String(c.d).padStart(2, '0')];
                  const holCls = hd ? (hd.k === 1 ? ' hol-shift' : hd.k === 2 ? ' hol-short' : ' hol') : '';
                  const cls = (ls.length ? ' has' : '') + (over ? ' over' : '') + (we ? ' we' : '') + holCls;
                  const holLabel = hd ? (hd.k === 1 ? 'Перенос выходного' : hd.k === 2 ? 'Сокращённый день' : 'Праздник') + ': ' + hd.n : '';
                  const tip = [dayTip(c.i), holLabel].filter(Boolean).join('\n') || undefined;
                  const holFlag = hd ? (hd.k === 1 ? '↩' : hd.k === 2 ? '−1ч' : '✦') : '';
                  return (
                    <div className={'cell' + cls} key={k} title={tip}>
                      <span className="n">{c.d}</span>
                      {hd && <span className={'hol-flag' + (hd.k === 2 ? ' short' : '')}>{holFlag}</span>}
                      {ls.length > 0 && (
                        <span className="marks">
                          {ls.slice(0, 3).map((u) => <i key={u} className="mdot" style={{ background: ultOf(u) }} />)}
                          {ls.length > 3 && <em>+{ls.length - 3}</em>}
                        </span>
                      )}
                    </div>
                  );
                })}
              </div>
            </div>
          ))}
        </div>
      )}
    </section>
  );
}
