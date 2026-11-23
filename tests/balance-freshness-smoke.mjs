// Run against the local Docker stack: node tests/balance-freshness-smoke.mjs
// Verifies immediately visible balance changes, including a leave spanning two years.
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const base = 'http://localhost:5000';
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const desktopDocker = `${process.env.LOCALAPPDATA}/Programs/DockerDesktop/resources/bin/docker.exe`;
const docker = existsSync(desktopDocker) ? desktopDocker : 'docker';
function redis(...args) {
  return execFileSync(docker, ['compose', 'exec', '-T', 'redis', 'redis-cli', ...args],
    { cwd: root, encoding: 'utf8' }).trim();
}
async function request(path, token, body) {
  const response = await fetch(base + path, {
    method: body === undefined ? 'GET' : 'POST',
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {})
    },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal: AbortSignal.timeout(10000)
  });
  const text = await response.text();
  assert.ok(response.ok, `${path}: HTTP ${response.status} ${text}`);
  return { data: text ? JSON.parse(text) : null, headers: response.headers };
}

const employee = (await request('/api/auth/login', null,
  { email: 'petr@vac.local', password: 'User123!' })).data;
const manager = (await request('/api/auth/login', null,
  { email: 'manager@vac.local', password: 'Manager123!' })).data;
const own = (await request('/api/leave', employee.token)).data;
let year = new Date().getUTCFullYear() + 2;
while (own.some(leave => [1, 2].includes(leave.status) &&
  Number(leave.startDate.slice(0, 4)) <= year + 1 && Number(leave.endDate.slice(0, 4)) >= year)) year += 2;

async function balance(y, expected) {
  const result = await request(`/api/balance/${employee.userId}?year=${y}`, employee.token);
  assert.equal(result.headers.get('cache-control'), 'no-store');
  if (expected !== undefined) assert.equal(result.data.remaining, expected, `Balance for ${y}`);
  const key = `balance:v2:${employee.userId}:${y}`;
  assert.equal(redis('EXISTS', key), '1', 'Balance must actually be cached in Redis');
  const ttl = Number(redis('TTL', key));
  assert.ok(ttl >= 0 && ttl <= 60, `Unexpected cache TTL: ${ttl}`);
  return result.data.remaining;
}

function invalidated(...years) {
  for (const y of years)
    assert.equal(redis('EXISTS', `balance:v2:${employee.userId}:${y}`), '0', `Stale cache for ${y}`);
}

const before = [await balance(year), await balance(year + 1)];
const createdIds = [];
try {
  const leave = (await request('/api/leave', employee.token, {
    userId: employee.userId, type: 0, startDate: `${year}-12-30`, endDate: `${year + 1}-01-02`,
    reason: 'Balance freshness regression: year boundary'
  })).data;
  createdIds.push(leave.id);
  invalidated(year, year + 1);
  await balance(year, before[0] - 2);
  await balance(year + 1, before[1] - 2);
  await request(`/api/leave/${leave.id}/approve`, manager.token, {});
  invalidated(year, year + 1);
  await balance(year, before[0] - 2);
  await request(`/api/leave/${leave.id}/cancel`, employee.token, {});
  invalidated(year, year + 1);
  await balance(year, before[0]);
  await balance(year + 1, before[1]);

  const draftId = (await request('/api/leave/draft', employee.token, {
    userId: employee.userId, type: 0, startDate: `${year}-03-01`, endDate: `${year}-03-02`,
    reason: 'Balance freshness regression: draft and decline'
  })).data;
  createdIds.push(draftId);
  await balance(year, before[0]);
  await request(`/api/leave/${draftId}/submit`, employee.token, {});
  invalidated(year);
  await balance(year, before[0] - 2);
  await request(`/api/leave/${draftId}/decline`, manager.token, {});
  invalidated(year);
  await balance(year, before[0]);
  console.log('PASS: Redis caches balance with TTL 60s; changes invalidate affected years; balances update immediately; no-store.');
} finally {
  const leaves = (await request('/api/leave', employee.token)).data;
  for (const leave of leaves.filter(item => createdIds.includes(item.id) && [0, 1, 2].includes(item.status))) {
    await request(`/api/leave/${leave.id}/cancel`, employee.token, {});
  }
}
