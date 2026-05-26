namespace MealPlanner.Models;

public class DismissedShoppingItem
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int IngredientBaseId { get; set; }
    public IngredientBase IngredientBase { get; set; } = null!;

    // When set, only this specific measurement is blocked (conflict resolution).
    // When null, the entire ingredient base is blocked (user-initiated removal).
    public int? MeasurementId { get; set; }
}
