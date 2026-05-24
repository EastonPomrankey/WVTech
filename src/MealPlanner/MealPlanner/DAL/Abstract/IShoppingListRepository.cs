using MealPlanner.Models;

namespace MealPlanner.DAL.Abstract;

public interface IShoppingListRepository
{
    void Add(ShoppingListItem item);
    void AddBatch(IEnumerable<ShoppingListItem> items);
    void AddAutoAddedBatch(IEnumerable<ShoppingListItem> items);

    // Also dismisses the ingredient base so it won't be re-added by future syncs.
    void Remove(int itemId, string userId);
    void PromoteToManual(string userId, IEnumerable<int> itemIds);

    void RemoveAllByIngredientBase(string userId, int ingredientBaseId);

    void RemoveAutoAddedByUserId(string userId);

    IEnumerable<ShoppingListItem> GetByUserId(string userId);

    void ClearAllItems(string userId);

    void UpdateAmountByIngredientBase(string userId, int ingredientBaseId, float newAmount, string? displayAmount = null);

    HashSet<int> GetDismissedIngredientBaseIds(string userId);
    HashSet<(int IngredientBaseId, int MeasurementId)> GetDeclinedMeasurementPairs(string userId);
    void DismissIngredientBase(string userId, int ingredientBaseId);
    void DismissBatch(string userId, IEnumerable<int> ingredientBaseIds);
    void DismissByMeasurement(string userId, int ingredientBaseId, int measurementId);
    void ClearMeasurementDeclines(string userId);
    void UnDismiss(string userId, int ingredientBaseId);
    void DeleteWithoutDismiss(int itemId, string userId);

    bool UpdateMeasurementById(string userId, int itemId, int measurementId);
    void UpdateAmountAndRecipeContribution(string userId, int itemId, float newAmount, float recipeContribution);
}
