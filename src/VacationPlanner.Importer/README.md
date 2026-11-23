# VacationPlanner.Importer

Одноразовая CLI-команда подаёт отпуска владельца JWT через `POST /api/leave`. В отдельной PostgreSQL хранятся задачи, ошибки строк и журнал отправок с идентификаторами созданных заявок. Для просмотра истории достаточно доступа к БД импортёра.

## Запуск с хоста

Команды ниже рассчитаны на PowerShell в корне репозитория, .NET SDK 10 и работающий Vacation Planner. Для стандартного Compose gateway доступен на порту 5000. Отдельную PostgreSQL можно запустить профилем импортёра. Для первого старта задайте собственный пароль новой БД.

```powershell
$env:IMPORTER_DB_PASSWORD = (Get-Credential -UserName importer -Message 'Пароль БД импортёра').GetNetworkCredential().Password
docker compose --profile importer up -d importer-db
$env:ConnectionStrings__Importer = 'Host=127.0.0.1;Port=55432;Database=vacation_importer;Username=importer'
$env:PGPASSWORD = $env:IMPORTER_DB_PASSWORD
$project = './src/VacationPlanner.Importer'
dotnet run --project $project -- migrate
```

Для существующего PostgreSQL укажите в `ConnectionStrings__Importer` выделенную базу. Команда `migrate` применяет миграции EF Core. Для создания отсутствующей базы роли потребуется CREATEDB. Можно заранее создать пустую базу и дать роли права на создание таблиц в её схеме.

Получите пользовательский JWT штатным входом. Используйте адрес своего gateway или Web API. Вне локальной демонстрационной сети используйте HTTPS.

```powershell
$env:Importer__BaseUrl = 'http://localhost:5000/'
$credential = Get-Credential -Message 'Почта и пароль пользователя Vacation Planner'
$body = @{email=$credential.UserName; password=$credential.GetNetworkCredential().Password} | ConvertTo-Json
$login = Invoke-RestMethod -Method Post -Uri ($env:Importer__BaseUrl + 'api/auth/login') -ContentType 'application/json' -Body $body
$env:Importer__Token = $login.token
$login.userId
```

Подставьте этот UserId в копию CSV. На чистом демонстрационном стенде с сотрудником 2 можно использовать примеры непосредственно.

```powershell
dotnet run --project $project -- import --file "$project/samples/leaves-valid.csv"
dotnet run --project $project -- import --file "$project/samples/leaves-mixed.csv"
dotnet run --project $project -- import --file "$project/samples/leaves-invalid.csv"
dotnet run --project $project -- list
dotnet run --project $project -- status --job 2
dotnet run --project $project -- errors --job 2
dotnet run --project $project -- dispatches --job 2
dotnet run --project $project -- recover
```

На пустой БД и свободных датах смешанный пример даёт `DoneWithErrors Total=3 Imported=1 Errors=2`. В строке 3 неверная дата, строка 4 пересекается с первой заявкой этого файла. Полный отчёт включает JobId, времена UTC, счётчики и диагностику. Exit codes: 0 — успех, 2 — ошибка ввода, 3 — импорт завершён с ошибками строк, 4 — инфраструктурный сбой, 130 — отмена. Ctrl+C останавливает приём и отправку, дожидается активных workers и сохраняет Canceled. Команда status отображает сохранённый результат и возвращает 0 при успешном чтении.

## Запуск в Docker

Из корня репозитория запустите основной стек, задайте тот же `IMPORTER_DB_PASSWORD` и получите `Importer__Token`, как показано выше. Профиль importer включает собственную БД и том importerdata. CSV монтируются только для чтения. По умолчанию это каталог samples, другой каталог задаётся абсолютным путём в `IMPORTER_INPUT_DIR`.

```powershell
docker compose up -d --build gateway
docker compose --profile importer build importer
docker compose --profile importer run --rm importer migrate
docker compose --profile importer run --rm importer import --file /input/leaves-valid.csv
docker compose --profile importer run --rm importer list
```

## Конфигурация

В [appsettings.json](appsettings.json) находятся значения по умолчанию. Переменные окружения переопределяют их с разделителем `__`.

| Параметр окружения | Значение по умолчанию или назначение |
|---|---|
| ConnectionStrings__Importer | Обязательная строка подключения к собственной БД |
| Importer__BaseUrl, Importer__Token | http://localhost:5000/ и access token для импорта |
| Importer__Workers, Importer__Capacity | 4 обработчика и очередь на 16 записей. Workers=1 даёт последовательный режим. |
| Importer__TimeoutSeconds | 30 секунд на HTTP-запрос |
| Importer__HeartbeatSeconds, Importer__StaleSeconds | 2 и 60 секунд |
| Importer__MaxFileBytes, Importer__MaxRows | 16 777 216 байт и 100 000 записей |

## Повтор, отмена и сбой

Перед POST импортёр атомарно резервирует RequestId в Dispatches. Подтверждённый HTTP 201 с корректным владельцем и LeaveId сохраняется вместе со счётчиком. Определённый бизнес-отказ сохраняется вместе с ошибкой строки. Повторный RequestId даёт ошибку Duplicate и ссылку на исходную задачу/строку. При отмене строки, до которых обработка ещё не дошла, остаются доступными следующему запуску.

Если ответ потерялся, процесс упал или БД отказала после отправки, запись остаётся Unconfirmed. Проверьте `dispatches --job <id>` и заявки владельца в основном приложении. У сервера нет поиска по RequestId. Решение о повторной подаче с новым UUID принимает оператор после сверки. Восстановление резервной копии БД импортёра откатывает и её журнал дедупликации. Автоматический retry POST отключён. Одна база импортёра должна обслуживать один целевой стенд API.

Одновременно работает одна команда import или recover на БД. PostgreSQL session advisory lock освобождается при разрыве соединения. Обычные команды чтения доступны во время импорта. При запуске import после preflight или явном recover задачи InProcess со старым HeartbeatAt (или StartedAt, если heartbeat ещё не было) переходят в Failed. После аварийного выхода дождитесь порога StaleSeconds и выполните recover. Терминальная задача сохраняется для аудита, повторный import получает новый JobId. При недоступной БД невозможно гарантировать запись терминального статуса до восстановления связи.

## Проверка и демонстрация OTUS

Все четыре технических критерия можно продемонстрировать в самом `VacationPlanner.Importer`.

| Критерий из ТЗ | Что показать в импортёре | Как проверить |
|---|---|---|
| База данных (10 баллов) | [JobStore](Persistence/JobStore.cs) резервирует UUID перед POST, затем сохраняет подтверждённый результат и счётчик в одной транзакции. [ImporterDbContext](Persistence/ImporterDbContext.cs) описывает собственные таблицы и миграции. | `SequentialMixedAndReplay` применяет миграцию к пустой PostgreSQL, перечитывает задачу, ошибку и ID заявки, затем проверяет повторный импорт. |
| Асинхронность (5 баллов) | От чтения CSV до HTTP и записи в БД передаётся `CancellationToken`. При отмене уже полученный ответ сохраняется с отдельным ограниченным по времени токеном. | `HeartbeatCancellationAndRestart` отменяет ожидающий HTTP-вызов, проверяет сохранение Canceled и запускает файл повторно. |
| Примитивы синхронизации (15 баллов) | В [RowPipeline](RowPipeline.cs) ограниченный `Channel<T>` останавливает чтение при заполнении очереди. `SemaphoreSlim` сериализует запись результатов. Для зависимых строк продюсер ждёт завершения предыдущей записи. | `BoundedWorkersPersistEveryOutcomeOnce` проверяет четыре одновременных запроса и результат всех 160 строк. `ConflictingRowsPreserveCsvOrder` проверяет порядок конфликтующих заявок. |
| Три паттерна (30 баллов) | Facade, Adapter и Iterator в рабочем пути команды `import`. | Пройти цепочку от вызова CLI до ответа API по таблице ниже. |

| Паттерн | Участники и задача | Наблюдаемое поведение |
|---|---|---|
| **Facade (Фасад)** | Команда `import` в [Program.cs](Program.cs) вызывает [ImportRunner.RunAsync](ImportRunner.cs). За одним вызовом стоят проверка файла, получение блокировки, регистрация задачи, запуск обработчиков и сохранение итогового статуса. | `InvalidPreflightLeavesNoJob` вызывает фасад с пустым CSV и проверяет отсутствие задачи. `SequentialMixedAndReplay` через тот же метод получает задачу с частичным успехом. |
| **Adapter (Адаптер)** | [LeaveApi.SubmitAsync](Http/LeaveApi.cs) переводит заявку строки в JSON для `POST /api/leave`, а HTTP-статус и тело ответа — в `ApiResult` либо `ApiFailure`. Обработчик строки получает ID созданной заявки или причину отказа. | `ApiTests.SendsOnceAndClassifies` проверяет метод, маршрут и сериализацию, затем результаты при 201, бизнес-отказе и сбое сервиса. |
| **Iterator (Итератор)** | [CsvSnapshot.RowsAsync](Csv/CsvSnapshot.cs) выдаёт по одному `CsvRecord` через `yield return`. При предварительной проверке записи считает `OpenAsync`, при импорте их читает продюсер через `await foreach`. Кавычки, многострочные поля и номера физических строк обрабатываются внутри итератора. | `ReadsOriginalSnapshotWithBomQuotesAndPhysicalLineNumbers` проверяет повторный проход после удаления исходника, содержимое многострочного поля и номер следующей записи. |

Для критерия объёма работы участника (30 баллов) предъявите собственный результат целиком: CLI, CSV, HTTP-интеграцию, схему БД, дедупликацию, параллельную обработку, восстановление и тесты. Состав команды и распределение ответственности подтверждаются на защите. Ещё 10 баллов относятся к фактическому сроку защиты.

Из корня репозитория выполните проверку проекта тестов импортёра. Ссылка на основной проект в тестовом проекте включает его в restore и build.

```powershell
$tests = './src/VacationPlanner.Importer/tests/VacationPlanner.Importer.Tests.csproj'
dotnet restore $tests
dotnet build $tests --no-restore
$env:IMPORTER_TEST_POSTGRES = 'Host=127.0.0.1;Port=55432;Database=postgres;Username=importer'
dotnet test $tests --no-build
```

Интеграционные тесты создают собственные базы со случайными именами, применяют миграции и удаляют эти базы после теста. Роли нужен CREATEDB. Пароль может поступать из PGPASSWORD. Без IMPORTER_TEST_POSTGRES эти тесты помечаются как пропущенные.

[Формат CSV](../../docs/importer/CSV_FORMAT.md).
