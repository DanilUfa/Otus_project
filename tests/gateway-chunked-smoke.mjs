// Run against the local Docker stack: node tests/gateway-chunked-smoke.mjs
// Both requests use chunked bodies, as does HttpClient.PostAsJsonAsync in the importer.
import http from 'node:http';
import assert from 'node:assert/strict';

function post(path, body, token) {
  return new Promise((resolve, reject) => {
    const request = http.request({
      hostname: 'localhost', port: 5000, path, method: 'POST', timeout: 10000,
      headers: {
        'Content-Type': 'application/json', 'Transfer-Encoding': 'chunked',
        ...(token ? { Authorization: `Bearer ${token}` } : {})
      }
    }, response => {
      let text = '';
      response.setEncoding('utf8');
      response.on('data', chunk => { text += chunk; });
      response.on('end', () => {
        try { resolve({ status: response.statusCode, body: JSON.parse(text) }); }
        catch { reject(new Error(`Non-JSON response, HTTP ${response.statusCode}`)); }
      });
    });
    request.on('error', reject);
    request.on('timeout', () => request.destroy(new Error('Gateway request timed out')));
    const json = JSON.stringify(body);
    const middle = Math.floor(json.length / 2);
    request.write(json.slice(0, middle));
    request.end(json.slice(middle));
  });
}

const login = await post('/api/auth/login', { email: 'petr@vac.local', password: 'User123!' });
assert.equal(login.status, 200, 'Chunked login must reach API');
assert.ok(login.body.token);
const invalidLeave = await post('/api/leave', {
  userId: login.body.userId, type: 0,
  startDate: '2000-01-01', endDate: '2000-01-02', reason: 'Chunked proxy regression check'
}, login.body.token);
assert.equal(invalidLeave.status, 400, 'API must reject past annual leave without creating data');
assert.match(invalidLeave.body.error, /задним числом/);
console.log('PASS: chunked login and leave body forwarded; API validation reached; no leave created.');
