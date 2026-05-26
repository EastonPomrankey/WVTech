using MealPlanner.Models;

namespace MealPlanner.Services;

public interface IPantryService
{
    List<Ingredient> GetPantryItems(string userId);
    Ingredient BuildPantryItem(string name, float amount, string measurement);
    void AddPantryItem(string userId, Ingredient item);
    void RemovePantryItem(int ingredientId, string userId);
    void UpdatePantryItemAmount(int ingredientId, string userId, float newAmount);

    HashSet<int> GetMealPantryMatchIds(string userId, List<Meal> meals);
    Task AutoRemovePantryItems(string userId, int mealId, DateTime completionDate, List<Meal> meals);
    bool HasAutoRemovedIngredients(int mealId, DateTime completionDate);
    void RestorePantryItems(string userId, int mealId, DateTime completionDate);
}
