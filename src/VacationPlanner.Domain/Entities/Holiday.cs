namespace VacationPlanner.Domain.Entities;

/// <summary>Нерабочий праздничный день (справочник для отображения в календаре).</summary>
public class Holiday
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string Name { get; set; } = "";
}
