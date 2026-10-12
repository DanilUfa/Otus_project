namespace VacationPlanner.Application;

/// <summary>Нарушение бизнес-правила/валидации — HTTP 400 Bad Request.</summary>
public class DomainValidationException(string message) : Exception(message);

/// <summary>Сущность не найдена — HTTP 404 Not Found.</summary>
public class NotFoundException(string message) : Exception(message);
