export const TYPE_LABELS = { 0: 'Ежегодный', 1: 'Больничный', 2: 'Без сохранения' };
export const TYPE_VALUES = { Annual: 0, Sick: 1, Unpaid: 2 };

export const STATUS_LABELS = {
  0: 'Черновик',
  1: 'На согласовании',
  2: 'Утверждён',
  3: 'Отклонён',
  4: 'Отменён'
};

// ---------- Сессия: access + refresh в localStorage ----------
const token = () => localStorage.getItem('token');
const refreshToken = () => localStorage.getItem('refreshToken');

// Эндпоинты, для которых 401 не означает «сессия истекла»
// (login — неверный пароль; refresh/logout — сами про токен).
const AUTH_PATHS = new Set(['/api/auth/login', '/api/auth/refresh', '/api/auth/logout']);

// Слушатели «сессия истекла» (refresh не удался — UI должен вернуться к экрану входа).
const sessionExpiredHandlers = [];
export function onSessionExpired(handler) { sessionExpiredHandlers.push(handler); }

function sessionExpired() {
  localStorage.removeItem('token');
  localStorage.removeItem('refreshToken');
  localStorage.removeItem('user');
  sessionExpiredHandlers.forEach((h) => h());
}

// Единая «in-flight» ротация: параллельные 401 ждут один запрос /auth/refresh,
// чтобы не слать N одновременных ротаций (из которых N-1 получат 401 из-за одноразовости).
let refreshPromise = null;
async function rotateRefresh() {
  const rt = refreshToken();
  if (!rt) return false;
  if (!refreshPromise) {
    refreshPromise = (async () => {
      try {
        const res = await fetch('/api/auth/refresh', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ refreshToken: rt })
        });
        if (!res.ok) return null;
        return await res.json();
      } catch { return null; }
    })().finally(() => { refreshPromise = null; });
  }
  const fresh = await refreshPromise;
  if (!fresh) return false;

  localStorage.setItem('token', fresh.token);
  localStorage.setItem('refreshToken', fresh.refreshToken);
  // Обновляем и «user» (остальные поля не меняются, но пусть будет консистентно).
  const stored = JSON.parse(localStorage.getItem('user') || 'null');
  if (stored) {
    localStorage.setItem('user', JSON.stringify({
      ...stored,
      token: fresh.token,
      refreshToken: fresh.refreshToken,
      userId: fresh.userId,
      name: fresh.name,
      role: fresh.role
    }));
  }
  return true;
}

async function request(path, { method = 'GET', body, formData } = {}) {
  // formData: multipart (загрузка файлов) — браузер сам ставит Content-Type с boundary.
  const headers = token() ? { Authorization: `Bearer ${token()}` } : {};
  if (!formData) headers['Content-Type'] = 'application/json';
  const attempt = () => fetch(path, {
    method,
    headers,
    body: formData || (body ? JSON.stringify(body) : undefined)
  });

  let res = await attempt();
  // Access-токен истёк (TTL 15 мин): одна ротация refresh-токена и повтор исходного запроса.
  if (res.status === 401 && !AUTH_PATHS.has(path) && refreshToken()) {
    if (await rotateRefresh()) {
      res = await attempt();
    }
  }
  if (res.status === 401) {
    if (!AUTH_PATHS.has(path)) sessionExpired();
    throw new Error('Требуется авторизация');
  }
  if (!res.ok) {
    const text = await res.text().catch(() => '');
    throw new Error(text || `Ошибка ${res.status}`);
  }
  return res.status === 204 ? null : res.json();
}

export const api = {
  login: (email, password) =>
    request('/api/auth/login', { method: 'POST', body: { email, password } }),

  // Отзыв refresh-токена на сервере (best-effort: даже если токен уже невалиден — чтим локаль).
  logout: () => {
    const rt = refreshToken();
    return rt
      ? request('/api/auth/logout', { method: 'POST', body: { refreshToken: rt } }).catch(() => null)
      : Promise.resolve(null);
  },

  users: () => request('/api/users'),
  leaves: () => request('/api/leave'),
  createLeave: (userId, type, startDate, endDate, reason) =>
    request('/api/leave', { method: 'POST', body: { userId, type, startDate, endDate, reason } }),
  saveDraft: (userId, type, startDate, endDate, reason) =>
    request('/api/leave/draft', { method: 'POST', body: { userId, type, startDate, endDate, reason } }),
  submitDraft: (id) => request(`/api/leave/${id}/submit`, { method: 'POST' }),
  cancelLeave: (id) => request(`/api/leave/${id}/cancel`, { method: 'POST' }),
  approve: (id) => request(`/api/leave/${id}/approve`, { method: 'POST' }),
  decline: (id) => request(`/api/leave/${id}/decline`, { method: 'POST' }),
  balance: (userId, year) => request(`/api/balance/${userId}?year=${year}`),
  // Read-модель (CQRS): календарь отдела из микросервиса reporting
  reportCalendar: (year) => request(`/api/reports/calendar?year=${year}`),
  holidays: (year) => request(`/api/holidays?year=${year}`),
  // История операций из микросервиса audit
  audit: (requestId) => request(`/api/audit${requestId ? `?requestId=${requestId}` : ''}`),

  // Документы к заявке (приложения: больничный лист, справка)
  attachments: (leaveId) => request(`/api/leave/${leaveId}/attachments`),
  uploadAttachment: (leaveId, file) => {
    const fd = new FormData();
    fd.append('file', file, file.name);
    return request(`/api/leave/${leaveId}/attachments`, { method: 'POST', formData: fd });
  },
  deleteAttachment: (id) => request(`/api/leave/attachments/${id}`, { method: 'DELETE' }),
  downloadAttachment: async (id, fileName) => {
    const headers = token() ? { Authorization: `Bearer ${token()}` } : {};
    const res = await fetch(`/api/leave/attachments/${id}/download`, { headers });
    if (!res.ok) throw new Error('Не удалось скачать документ (' + res.status + ')');
    const blob = await res.blob();
    const aEl = document.createElement('a');
    aEl.href = URL.createObjectURL(blob);
    aEl.download = fileName || 'document';
    document.body.appendChild(aEl); aEl.click(); aEl.remove();
    URL.revokeObjectURL(aEl.href);
  }
};
