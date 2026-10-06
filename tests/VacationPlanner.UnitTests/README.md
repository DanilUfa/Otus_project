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
На машине с доступом к NuGet (nuget.org):
    dotnet test tests/VacationPlanner.UnitTests

На рабочей машине корпоративный прокси режет nuget.org, поэтому здесь юнит-проект не восстановится.
Офлайн-двойник тех же сценариев (без внешних пакетов):
    dotnet run --project tests/VacationPlanner.Smoke

## Примечание
Проект НЕ добавлен в VacationPlanner.sln намеренно — иначе `dotnet build` решения стал бы требовать
restore тестовых пакетов. Добавить в sln:  dotnet sln add tests/VacationPlanner.UnitTests
