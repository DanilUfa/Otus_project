import assert from 'node:assert/strict';
const base = process.env.BASE_URL || 'http://localhost:5000';
const post = (path, body) => fetch(base + path, { method: 'POST',
  headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body), signal: AbortSignal.timeout(10000) });
const login = await post('/api/auth/login', { email: 'petr@vac.local', password: 'User123!' });
assert.equal(login.status, 200);
const session = await login.json();
let replacement;
try {
  const responses = await Promise.all([1, 2].map(() => post('/api/auth/refresh', { refreshToken: session.refreshToken })));
  assert.deepEqual(responses.map(r => r.status).sort(), [200, 401]);
  replacement = await responses.find(r => r.ok).json();
  assert.equal((await post('/api/auth/refresh', { refreshToken: session.refreshToken })).status, 401);
  console.log('PASS: exactly one concurrent refresh succeeds; original token cannot be replayed');
} finally {
  await post('/api/auth/logout', { refreshToken: replacement?.refreshToken || session.refreshToken });
}
