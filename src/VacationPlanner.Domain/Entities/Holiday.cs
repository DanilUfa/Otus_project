namespace VacationPlanner.Domain.Entities;

/// <summary>День производственного календаря (справочник для отображения в календаре).</summary>
public class Holiday
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    /// <summary>Название дня (например, «Новогодние каникулы»).</summary>
    public string Name { get; set; } = "";
    /// <summary>0 — нерабочий праздничный день, 1 — перенос выходного, 2 — сокращённый предпраздничный день.</summary>
    public int Kind { get; set; }
}
