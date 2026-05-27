using MealPlanner.Models;

namespace MealPlanner.Services;


public interface IShoppingListService
{
    IEnumerable<ShoppingListItem> GetItemsForUser(string userId);
    void AddItem(string userId, string itemName, float amount, string measurement, string? displayAmount = null);
    void AddItemsBatch(string userId, IEnumerable<(string name, float amount, string measurement)> items);
    void RemoveItem(int itemId, string userId);
    void ResolveAutoAddedConflicts(string userId, IEnumerable<int> autoItemIds);
    void ClearItems(string userId);
    void UpdateItemAmount(string userId, int ingredientBaseId, float newAmount, string? displayAmount = null);
    void ClearMeasurementDeclines(string userId);
    Task SyncFromDateRangeAsync(string userId, User user, DateTime dateFrom, DateTime dateTo);
    Task<string?> UpdateItemMeasurementAsync(string userId, int itemId, string measurementName);
    Task<List<Measurement>> GetMeasurementsAsync();
    IEnumerable<ShoppingListItem> FindConflictingItems(string userId, string ingredientName, string addedMeasurementName);
    IEnumerable<(ShoppingListItem AutoAdded, ShoppingListItem Manual)> FindAutoAddedConflicts(string userId, IReadOnlyList<ShoppingListItem>? cachedItems = null);
    void UnDismissIngredientBases(string userId, IEnumerable<int> ingredientBaseIds);
}
