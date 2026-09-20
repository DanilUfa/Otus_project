# Планирование отпусков — черновой вариант (MVP)

C# / .NET 10 · ASP.NET Core Minimal API · PostgreSQL · EF Core · MassTransit (RabbitMQ) · Redis (кэш) · Swagger (Swashbuckle).

## Шаблоны проектирования (что реализовано в коде)

> Список — из программы курса. Ниже только те, что **есть в коде**, с указанием классов —
> чтобы на защите можно было показать точное место.

| # | Паттерн | Группа | Где в коде | Что даёт |
|---|---------|--------|-----------|----------|
| 1 | **Factory Method** | создание | `Application/Patterns/LeaveRequestFactory.cs` | единая точка создания `LeaveRequest` (тип/даты/статус), скрывает инициализацию |
| 2 | **Builder** | создание | `Application/Patterns/LeaveRequestBuilder.cs` | пошаговое построение заявки (`ForUser().OfType().Between().Reason().Build()`), читаемо и валидируется |
| 3 | **Observer** | поведение | доменные события `Events/*` + `Worker/Consumers/*` | подписчики узнают о событиях без прямой связи с источником |
| 4 | **Mediator** | поведение | **MassTransit/RabbitMQ** (`LeaveRequestSubmitted`, `LeaveRequestStatusChanged`) | вынесение взаимодействия между компонентами за шину: API публикует, Worker слушает |
| 5 | **Adapter** | структура | `EmailNotificationSender` (MailKit ↔ `INotificationSender`); `LeaveRepository/UserRepository` (EF Core ↔ `ILeaveRepository/IUserRepository`) | сведение сторонней библиотеки/ORM к нашим интерфейсам |
| 6 | **Singleton** | создание | `BalanceGate`, `VacationMetrics` — регистрации `AddSingleton` в DI | одно общее состояние (per-user async-замок; счётчики метрик) |
| 7 | **State** | поведение | `Application/Patterns/LeaveState.cs` + use в `LeaveService` | жизненный цикл заявки: объекты-состояния запрещают нелегальные переходы (например, нельзя утвердить уже отклонённую) |

### Как объяснить **Observer + Mediator** на защите
- **Издатель** (API в `LeaveService`) после сохранения заявки вызывает `IPublishEndpoint.Publish(...)`.
- **Подписчики** (Worker: `LeaveRequestSubmittedConsumer`, `LeaveRequestStatusChangedConsumer`) сами не знают про издателя — регистрируются в `AddMassTransit`.
- Транспорт (RabbitMQ) — это и есть медиатор: продюсер и консьюмер связаны только контрактом сообщения, а не кодом.
- Проверка «вживую»: подаём заявку → в `logs/worker.log` видим пересчёт баланса и отправку письма; событие ловится консьюмером, хотя API «не знает» о Worker.

### Дополнительно (усиливает аргумент, но не считается основным)
- `SemaphoreSlim` (`BalanceGate.LockAsync`) — **примитив синхронизации** (асинхронный замок на пользователя, защищает пересчёт баланса от гонок).
- **Repository + DI** — изоляция доступа к данным за интерфейсами (`ILeaveRepository`, `IUserRepository`).

> Честная заметка для защиты: `Decorator`/`Memento`, Keycloak, Kafka — в **роадмапе** (см. `ARCHITECTURE.md`), в этом MVP не реализованы.

## Быстрый запуск одной командой (Docker)

```bash
docker compose up -d --build
```

| Что | Где |
|-----|-----|
| Swagger API | http://localhost:5000/swagger |
| SPA (/ или index) | http://localhost:5000/ |
| RabbitMQ UI | http://localhost:15672 (vac / vac) |

После первого подъёма создаются тестовые пользователи (менеджер и сотрудник).
Схема БД управляется миграциями EF Core (`db.Database.Migrate()` на старте).

## Frontend (React SPA)

```text
src/VacationPlanner.Web/ClientApp/    # Vite + React
```
Функции: вход, подача заявки, список, согласование (менеджер), остаток дней, годовой календарь-график.

**Режим разработки** (Vite dev-server на :5173, проксирует `/api` на :5000):
```bash
npm --prefix src/VacationPlanner.Web/ClientApp install
npm --prefix src/VacationPlanner.Web/ClientApp run dev
```
Открыть http://localhost:5173

Тестовый вход: `manager@vac.local / Manager123!` или `petr@vac.local / User123!`.

**Продакшен**: `npm run build` собирает SPA в `wwwroot`, и API отдаёт `/` как SPA.

## Если сборка в Docker падает с «NU1301 ... UntrustedRoot»

Корпоративная SSL-проверка (прокси): внутри linux-контейнера нет вашего корневого CA, поэтому `dotnet restore` из `api.nuget.org` не доверяет TLS.

**Быстрый путь (приложение на хосте, инфраструктура в Docker):**
```powershell
docker compose up -d postgres redis rabbitmq
dotnet restore
dotnet build VacationPlanner.sln
dotnet run --project src/VacationPlanner.Web      # http://localhost:5000/swagger
dotnet run --project src/VacationPlanner.Worker   # отдельное окно
```

## Мониторинг: Prometheus + Grafana

Бизнес-метрики отдаются API на **`GET /metrics`** (формат Prometheus, `text/plain; version=0.0.4`), реализованы на `System.Diagnostics.Metrics` (Meter + счётчики; `MeterListener` копит кумулятивные значения).

| Метрика | Смысл |
|---------|-------|
| `vacation_leaves_submitted_total` | всего подано заявок |
| `vacation_leaves_approved_total`  | всего утверждено |
| `vacation_leaves_declined_total`  | всего отклонено |

**Запуск мониторинга** (API при этом работает на хосте на `:5000`):
```powershell
docker compose up -d prometheus grafana
```
- Prometheus: http://localhost:9090 (скрейпит `host.docker.internal:5000/metrics`)
- Grafana:  http://localhost:3000  — **admin/admin**, датасорс Prometheus и дашборд «Планирование отпусков» подключены автоматически (`grafana/`).

Проверка endpoint: `Invoke-RestMethod http://localhost:5000/metrics`

## Уведомления по email (Mailpit)

Для разработки письма шлются в **Mailpit** — локальный SMTP-«ловец» (без реального ящика и паролей приложения). Поднимается вместе с инфрой (`docker compose up -d mailpit`).

- SMTP: `localhost:1025` (без TLS/авторизации) — так и настроено в `src/VacationPlanner.Worker/appsettings.json`.
- Веб-морда: http://localhost:8025 — все отправленные уведомления видны тут.

Worker шлёт руководителю («новая заявка») и сотруднику («утверждена/отклонена»). В логе `logs/worker.log` появится `Уведомление отправлено на ...`.

Для реального почтовика (Outlook/Office365): задайте `Notifications__Email__Host=smtp.office365.com`, `Port=587`, `StartTls=true`, `Username`/`Password` и `From` (адрес ящика).

*Простые sanity-проверки инкрементов: подать заявку → `vacation_leaves_submitted_total` растёт; утвердить → `_approved_total` растёт.*

**Правильный путь (доверить корпоративный CA контейнеру):**
1. В браузере откройте `https://api.nuget.org` → сертификат → «Путь сертификации» → верх (корень).
2. Экспортируйте его в Base64 и сохраните как `certs/corp-root.crt`.
3. В `Dockerfile` (build-стадия) добавьте:
   ```dockerfile
   COPY certs/corp-root.crt /usr/local/share/ca-certificates/corp-root.crt
   RUN update-ca-certificates
   ```
4. `docker compose up -d --build`.

## Эндпоинты
Все, кроме `/api/auth/login`, требуют токен `Authorization: Bearer <token>`.

### Авторизация (JWT + роли)
Тестовые пользователи (пароль указан):
- руководитель: `manager@vac.local` / `Manager123!`
- сотрудник: `petr@vac.local` / `User123!`

Пример:
```bash
# вход → получаем токен
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"manager@vac.local","password":"Manager123!"}'

export TOKEN="eyJ..."
curl -H "Authorization: Bearer $TOKEN" http://localhost:5000/api/users
```

### Заявки
- `GET /api/users` — список сотрудников (auth)
- `GET /api/leave` — все заявки (auth)
- `POST /api/leave` — подать заявку: `{ userId, type, startDate, endDate, reason }` (auth)
  → заявка получает статус `Submitted`, публикуется событие, Worker шлёт уведомление руководителю на email
- `POST /api/leave/{id}/approve` — утвердить (**роль Manager**, и только руководитель сотрудника)
- `POST /api/leave/{id}/decline` — отклонить (**роль Manager**)
- `POST /api/leave/draft` — сохранить черновик (статус `Draft`, без уведомлений)
- `POST /api/leave/{id}/submit` — отправить черновик на согласование (`Draft → Submitted`, автор)
- `POST /api/leave/{id}/cancel` — отменить черновик/поданную заявку (`→ Cancelled`, автор)
- `GET /api/balance/{userId}?year=2026` — остаток дней годового отпуска (auth, кэш Redis TTL 60с)
- `GET /health/live` — liveness
- `GET /health/ready` — readiness (БД + RabbitMQ + Redis)

Пример подачи заявки сотрудником:
```bash
curl -X POST http://localhost:5000/api/leave \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"userId":2,"type":0,"startDate":"2026-07-01","endDate":"2026-07-14","reason":"Отпуск летом"}'
```
`type`: 0 = Annual, 1 = Sick, 2 = Unpaid.

### Поток согласования
1. Сотрудник подаёт заявку → статус `Submitted`, Worker уведомляет руководителя.
2. Руководитель подтверждает ролью Manager и вызывает `/approve` или `/decline`
   (проверка: утверждающим может быть только непосредственный руководитель сотрудника).
3. Статус меняется на `Approved`/`Declined`; `GET /api/balance` учитывает только утверждённый годовой отпуск.

### Уведомление на Outlook
При подаче заявки Worker отправляет руководителю email (SMTP/Outlook через MailKit). Для работы пропишите SMTP в `Notifications:Email` (в compose-переменных выше). Если SMTP не задан — уведомление пропускается с логом (приложение не падает).

### Смоук-тест (весь поток)
```bash
docker compose up -d --build
sleep 8

BASE=http://localhost:5000

# 1) Сотрудник входит
EMP=$(curl -s $BASE/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"petr@vac.local","password":"User123!"}' | jq -r .token)

# 2) Сотрудник подаёт заявку (Annual = type 0)
curl -s -X POST $BASE/api/leave -H "Content-Type: application/json" \
  -H "Authorization: Bearer $EMP" \
  -d '{"userId":2,"type":0,"startDate":"2026-07-01","endDate":"2026-07-14","reason":"Отпуск"}'

# 3) Руководитель входит и смотрит заявки
MGR=$(curl -s $BASE/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"manager@vac.local","password":"Manager123!"}' | jq -r .token)
curl -s $BASE/api/leave -H "Authorization: Bearer $MGR"

# 4) Руководитель утверждает заявку №1
curl -s -X POST $BASE/api/leave/1/approve -H "Authorization: Bearer $MGR"

# 5) Остаток дней сотрудника (годовой)
curl -s "$BASE/api/balance/2?year=2026" -H "Authorization: Bearer $EMP"
```
Worker в логах покажет пересчёт `остаток ... = N дн.` и, при настроенном SMTP, отправку письма руководителю.

## Локальная разработка (без Docker для API/Worker)

Поднять инфраструктуру, потом запускать из IDE:

```bash
docker compose up -d postgres redis rabbitmq
dotnet restore
dotnet run --project src/VacationPlanner.Web      # http://localhost:5000
dotnet run --project src/VacationPlanner.Worker
```

## Структура

```
src/
├─ VacationPlanner.Domain/        # сущности, enums, событие
├─ VacationPlanner.Application/   # DTO, репозитории-инт., Factory+Builder, сервисы
├─ VacationPlanner.Infrastructure/ # AppDbContext (PostgreSQL), репозитории, DI
├─ VacationPlanner.Web/           # Minimal API + Swagger + MassTransit (publish)
└─ VacationPlanner.Worker/        # консьюмер события MassTransit
```

## Что дальше (план доработок)

- [x] React SPA (раздаётся API), годовой календарь-график отпусков
- [x] Реальный пересчёт баланса в Worker + примитивы синхронизации (SemaphoreSlim)
- [x] Паттерн State (жизненный цикл заявки), черновик → отправка → отмена
- [x] Redis-кэш баланса и списка пользователей
- [x] Health-эндпоинты (`/health/live`, `/health/ready`)
- [x] Realtime (SignalR): SPA обновляется при новой заявке/смене статуса
- [x] Аудит-консьюмер (fan-out по той же шине) + авто-закрытие «зависших» черновиков
- [ ] Decorator (аудит через перехват уведомлений)
- [ ] Keycloak (OIDC) вместо встроенного JWT (сейчас — локальный JWT с ролями)
- [ ] Kafka как второй транспорт MassTransit