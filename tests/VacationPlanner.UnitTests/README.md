# VacationPlanner.UnitTests

Каркас unit-тестов: State-машина жизненного цикла заявки и расчёт остатка отпуска.

## Покрытие
- **LeaveStateTransitionsTests** — полная матрица переходов (разрешённые проходят, запрещённые бросают
  InvalidOperationException с текстом «не может перейти»), фабрика для неизвестного статуса, свойство Status.
- **BalanceServiceTests** — учитываются только Approved+Annual; дни включительно (Days=(end-start).Days+1);
  суммирование; кламп к 0; окно года передаётся репозиторию; границы года (заявка вне окна / на стыке годов);
  дубликат реальной SQL-семантики репозитория в FakeLeaveRepository (overlap: StartDate<=to && EndDate>=from).
- **BalanceGateTests** — сериализация per-user, изоляция разных пользователей, повторный захват, cancellation.

## Запуск
Проект добавлен в VacationPlanner.sln; тесты гоняются штатно:
    dotnet test tests/VacationPlanner.UnitTests
или из корня: dotnet test --filter VacationPlanner.UnitTests

Офлайн-двойник тех же сценариев (без внешних пакетов, работает и без restore):
    dotnet run --project tests/VacationPlanner.Smoke
