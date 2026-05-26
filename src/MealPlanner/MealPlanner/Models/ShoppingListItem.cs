namespace MealPlanner.Models;

public class ShoppingListItem
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public int IngredientBaseId { get; set; }
    public IngredientBase IngredientBase { get; set; } = null!;

    public int MeasurementId { get; set; }
    public Measurement Measurement { get; set; } = null!;

    public float Amount { get; set; }

    public string? DisplayAmount { get; set; }

    public bool IsAutoAdded { get; set; }

    // Tracks how much of Amount came from recipes (in base unit, e.g. tsp).
    // Used by sync to subtract the old recipe portion and apply the new one,
    // so removing a recipe restores just the user's manually-added quantity.
    public float RecipeContributionAmountInBase { get; set; }
}
