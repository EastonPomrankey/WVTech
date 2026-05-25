using MealPlanner.Models;
using MealPlanner.Services;
using Microsoft.IdentityModel.Tokens;

namespace MealPlanner.Helpers;

public static class MealCompletionListExtensions
{
    public static async Task LoadExternalRecipesAsync(this List<MealCompletion> completions, IExternalRecipeService? externalRecipeService)
    {
        if (externalRecipeService == null) return;
        
        var meals = completions.GroupBy(c => c.MealId).Select(g => g.First().Meal).ToList();
        await meals.LoadExternalRecipesAsync(externalRecipeService);

        for(int i=0; i<completions.Count; i++)
        {
            var completion = completions[i];
            completion.Meal = meals.FirstOrDefault(m => m.Id == completion.MealId) ?? new Meal();
        }
    }
}