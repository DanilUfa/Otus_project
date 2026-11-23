// Run after npm ci and Docker startup: node tests/calendar-realtime-smoke.mjs
// Another employee must receive an update AFTER Reporting commits the calendar projection.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
const require = createRequire(new URL('../src/VacationPlanner.Web/ClientApp/package.json', import.meta.url));
const { HubConnectionBuilder, HttpTransportType, LogLevel } = require('@microsoft/signalr');
const base = 'http://localhost:5000';

async function request(path, token, body) {
  const response = await fetch(base + path, {
    method: body === undefined ? 'GET' : 'POST',
    headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(10000)
  });
  const text = await response.text();
  assert.ok(response.ok, `${path}: HTTP ${response.status} ${text}`);
  return text ? JSON.parse(text) : null;
}
const login = email => request('/api/auth/login', null, { email, password: email.startsWith('manager') ? 'Manager123!' : 'User123!' });
const owner = await login('petr@vac.local');
const manager = await login('manager@vac.local');
const viewer = await login('maria@vac.local');
const leaves = await request('/api/leave', owner.token);
let year = new Date().getUTCFullYear() + 2;
while (leaves.some(l => [1, 2].includes(l.status) && Number(l.startDate.slice(0, 4)) <= year && Number(l.endDate.slice(0, 4)) >= year)) year++;

function connect(token) {
  return new HubConnectionBuilder()
    .withUrl(`${base}/hubs/vacation`,
      { transport: HttpTransportType.WebSockets, accessTokenFactory: () => token || '' })
    .configureLogging(LogLevel.None).build();
}
const observer = connect(viewer.token);
const anonymous = connect();
let anonymousEvents = 0;
anonymous.on('CalendarUpdated', () => anonymousEvents++);
const snapshots = [];
const waiters = new Set();
observer.on('CalendarUpdated', async event => {
  try {
    assert.deepEqual(Object.keys(event).sort(), ['fromYear', 'toYear']);
    if (event.fromYear > year || event.toYear < year) return;
    const snapshot = await request(`/api/reports/calendar?year=${year}`, viewer.token);
    snapshots.push(snapshot);
    for (const waiter of waiters) if (waiter.matches(snapshot)) waiter.resolve(snapshot);
  } catch (error) { for (const waiter of waiters) waiter.reject(error); }
});

async function waitForSnapshot(matches) {
  const saved = snapshots.find(matches);
  if (saved) return saved;
  let waiter;
  let timer;
  try {
    return await new Promise((resolve, reject) => {
      waiter = { matches, resolve, reject };
      waiters.add(waiter);
      timer = setTimeout(() => reject(new Error('CalendarUpdated did not provide the expected committed projection')), 20000);
    });
  } finally { clearTimeout(timer); waiters.delete(waiter); }
}

let leave;
try {
  await Promise.all([observer.start(), anonymous.start()]);
  leave = await request('/api/leave', owner.token, {
    userId: owner.userId, type: 2, startDate: `${year}-07-01`, endDate: `${year}-07-03`,
    reason: 'Calendar SignalR regression'
  });
  await waitForSnapshot(s => s.leaves.some(l => l.requestId === leave.id && l.status === 1));
  console.log('PASS: another employee received CalendarUpdated with the submitted leave already available.');
  await request(`/api/leave/${leave.id}/approve`, manager.token, {});
  await waitForSnapshot(s => s.leaves.some(l => l.requestId === leave.id && l.status === 2));
  console.log('PASS: approval refreshes the shared calendar after projection commit.');
  const beforeCancel = snapshots.length;
  await request(`/api/leave/${leave.id}/cancel`, owner.token, {});
  await waitForSnapshot(s => snapshots.indexOf(s) >= beforeCancel && !s.leaves.some(l => l.requestId === leave.id));
  assert.equal(anonymousEvents, 0, 'Unauthenticated connections must not receive calendar events');
  console.log('PASS: cancellation refreshes the calendar; anonymous connection received no events.');
} finally {
  await Promise.all([observer.stop(), anonymous.stop()]);
  if (leave) {
    const current = (await request('/api/leave', owner.token)).find(l => l.id === leave.id);
    if (current && [0, 1, 2].includes(current.status)) await request(`/api/leave/${leave.id}/cancel`, owner.token, {});
  }
}
