// Exercises the real client module with HTTP/storage doubles; no running server required.
import assert from 'node:assert/strict';
const storage = new Map();
globalThis.localStorage = {
  getItem: key => storage.get(key) ?? null,
  setItem: (key, value) => storage.set(key, String(value)),
  removeItem: key => storage.delete(key)
};
const { api } = await import('../src/VacationPlanner.Web/ClientApp/src/api.js');
const seed = () => { storage.set('token', 'OLD'); storage.set('refreshToken', 'REFRESH'); };
let rotations = 0;
const authorizations = [];
globalThis.fetch = async (path, options) => {
  if (path === '/api/auth/refresh') {
    rotations++;
    await new Promise(resolve => setTimeout(resolve, 10));
    return Response.json({ token: 'NEW', refreshToken: 'NEXT' });
  }
  authorizations.push(options.headers.Authorization);
  return options.headers.Authorization === 'Bearer NEW'
    ? Response.json([]) : new Response(null, { status: 401 });
};
seed();
await Promise.all([api.users(), api.leaves()]);
assert.equal(rotations, 1);
assert.deepEqual(authorizations, ['Bearer OLD', 'Bearer OLD', 'Bearer NEW', 'Bearer NEW']);
assert.equal(localStorage.getItem('token'), 'NEW');
assert.equal(localStorage.getItem('refreshToken'), 'NEXT');
seed();
globalThis.fetch = async () => new Response(null, { status: 401 });
await assert.rejects(api.users(), /авторизация/);
assert.equal(localStorage.getItem('token'), null);
console.log('PASS: concurrent requests share refresh, retry with new token, and clear rejected session');
