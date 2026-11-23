namespace VacationPlanner.Domain.Events;

/// <summary>The reporting projection is committed; authenticated calendar viewers may refresh.</summary>
public record CalendarUpdated(int FromYear, int ToYear);
