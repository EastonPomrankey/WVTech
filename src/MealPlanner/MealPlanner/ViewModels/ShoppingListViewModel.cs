using MealPlanner.Models;

namespace MealPlanner.ViewModels;

public record AutoAddedConflict(
    int AutoAddedItemId,
    float AutoAddedAmount,
    string AutoAddedAbbrev,
    string IngredientName,
    float ManualAmount,
    string ManualAbbrev);

public class ShoppingListViewModel
{
    public IEnumerable<ShoppingListItem> Items { get; set; } = [];
    public DateTime DateFrom { get; set; } = DateTime.Today;
    public DateTime DateTo { get; set; } = DateTime.Today;
    public string? ZipCode { get; set; }
    public string? LastStoreId { get; set; }
    public bool KrogerConnected { get; set; }
    public List<Measurement> Measurements { get; set; } = [];
    public List<AutoAddedConflict> AutoAddedConflicts { get; set; } = [];
}
