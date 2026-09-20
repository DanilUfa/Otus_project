export const TYPE_LABELS = { 0: 'Ежегодный', 1: 'Больничный', 2: 'Без сохранения' };
export const TYPE_VALUES = { Annual: 0, Sick: 1, Unpaid: 2 };

export const STATUS_LABELS = {
  0: 'Черновик',
  1: 'На согласовании',
  2: 'Утверждён',
  3: 'Отклонён',
  4: 'Отменён'
};

// Лёгкий клиент API: хранит токен в localStorage.
const token = () => localStorage.getItem('token');

async function request(path, { method = 'GET', body } = {}) {
  const res = await fetch(path, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token() ? { Authorization: `Bearer ${token()}` } : {})
    },
    body: body ? JSON.stringify(body) : undefined
  });

  if (res.status === 401) {
    localStorage.removeItem('token');
    localStorage.removeItem('user');
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
  balance: (userId, year) => request(`/api/balance/${userId}?year=${year}`)
};