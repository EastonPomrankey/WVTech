using MealPlanner.Helpers;
using MealPlanner.Models;
using MealPlanner.Services;
using Moq;

namespace MealPlanner.Tests;

public class MealCompletionListExtensionsTests
{
    private static Recipe MakeExternal(int id, string name, string uri) =>
        new Recipe { Id = id, Name = name, ExternalUri = uri };

    private static Recipe MakeLocal(int id, string name) =>
        new Recipe { Id = id, Name = name, ExternalUri = null };

    private static Recipe MakeLoaded(string name, string uri, int calories = 200) =>
        new Recipe { Name = name, ExternalUri = uri, Calories = calories, Directions = "" };

    private static Meal MakeMeal(int id, params Recipe[] recipes) =>
        new Meal { Id = id, Recipes = recipes.ToList() };

    private static MealCompletion MakeCompletion(Meal meal, DateTime date) =>
        new MealCompletion { MealId = meal.Id, Meal = meal, CompletionDate = date };

    [Test]
    public async Task LoadExternalRecipesAsync_DoesNothing_WhenServiceIsNull()
    {
        var meal = MakeMeal(1, MakeExternal(10, "Pasta", "http://uri/1"));
        var completions = new List<MealCompletion> { MakeCompletion(meal, DateTime.Today) };

        await MealCompletionListExtensions.LoadExternalRecipesAsync(completions, null);

        Assert.That(completions[0].Meal.Recipes[0].Name, Is.EqualTo("Pasta"));
    }

    [Test]
    public async Task LoadExternalRecipesAsync_DoesNothing_WhenListIsEmpty()
    {
        var completions = new List<MealCompletion>();
        var serviceMock = new Mock<IExternalRecipeService>();

        await MealCompletionListExtensions.LoadExternalRecipesAsync(completions, serviceMock.Object);

        serviceMock.Verify(
            s => s.GetExternalRecipesByURIs(It.IsAny<IEnumerable<string>>()),
            Times.Never);
    }

    [Test]
    public async Task LoadExternalRecipesAsync_UpdatesMealRecipes_WithLoadedData()
    {
        var meal = MakeMeal(1, MakeExternal(10, "Pasta Stub", "http://uri/1"));
        var completions = new List<MealCompletion> { MakeCompletion(meal, DateTime.Today) };

        var serviceMock = new Mock<IExternalRecipeService>();
        serviceMock
            .Setup(s => s.GetExternalRecipesByURIs(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([MakeLoaded("Pasta Full", "http://uri/1", calories: 500)]);

        await completions.LoadExternalRecipesAsync(serviceMock.Object);

        var recipe = completions[0].Meal.Recipes[0];
        Assert.That(recipe.Name, Is.EqualTo("Pasta Full"));
        Assert.That(recipe.Calories, Is.EqualTo(500));
    }

    [Test]
    public async Task LoadExternalRecipesAsync_DeduplicatesMeals_BeforeCallingService()
    {
        var meal = MakeMeal(1, MakeExternal(10, "Pasta", "http://uri/1"));
        var completions = new List<MealCompletion>
        {
            MakeCompletion(meal, DateTime.Today),
            MakeCompletion(meal, DateTime.Today.AddDays(-1)),
        };

        var serviceMock = new Mock<IExternalRecipeService>();
        serviceMock
            .Setup(s => s.GetExternalRecipesByURIs(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([MakeLoaded("Pasta Full", "http://uri/1")]);

        await completions.LoadExternalRecipesAsync(serviceMock.Object);

        serviceMock.Verify(
            s => s.GetExternalRecipesByURIs(It.Is<IEnumerable<string>>(
                uris => uris.Count() == 1 && uris.First() == "http://uri/1")),
            Times.Once);
    }

    [Test]
    public async Task LoadExternalRecipesAsync_UpdatesAllCompletions_SharingTheSameMeal()
    {
        var meal = MakeMeal(1, MakeExternal(10, "Pasta Stub", "http://uri/1"));
        var completions = new List<MealCompletion>
        {
            MakeCompletion(meal, DateTime.Today),
            MakeCompletion(meal, DateTime.Today.AddDays(-1)),
        };

        var serviceMock = new Mock<IExternalRecipeService>();
        serviceMock
            .Setup(s => s.GetExternalRecipesByURIs(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([MakeLoaded("Pasta Full", "http://uri/1")]);

        await completions.LoadExternalRecipesAsync(serviceMock.Object);

        Assert.That(completions[0].Meal.Recipes[0].Name, Is.EqualTo("Pasta Full"));
        Assert.That(completions[1].Meal.Recipes[0].Name, Is.EqualTo("Pasta Full"));
    }

    [Test]
    public async Task LoadExternalRecipesAsync_PreservesLocalRecipes_OnMeal()
    {
        var meal = MakeMeal(1, MakeLocal(5, "Local"), MakeExternal(10, "External Stub", "http://uri/1"));
        var completions = new List<MealCompletion> { MakeCompletion(meal, DateTime.Today) };

        var serviceMock = new Mock<IExternalRecipeService>();
        serviceMock
            .Setup(s => s.GetExternalRecipesByURIs(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([MakeLoaded("External Full", "http://uri/1")]);

        await completions.LoadExternalRecipesAsync(serviceMock.Object);

        var recipes = completions[0].Meal.Recipes;
        Assert.That(recipes.Any(r => r.Name == "Local"), Is.True);
        Assert.That(recipes.Any(r => r.Name == "External Full"), Is.True);
    }

    [Test]
    public async Task LoadExternalRecipesAsync_AssignsEmptyMeal_WhenMealIdNotFoundAfterLoad()
    {
        var meal1 = MakeMeal(1, MakeExternal(10, "Pasta", "http://uri/1"));
        var meal2 = MakeMeal(2, MakeExternal(20, "Soup", "http://uri/2"));

        var completion1 = MakeCompletion(meal1, DateTime.Today);
        var completion2 = MakeCompletion(meal2, DateTime.Today);
        // Manually change MealId so it won't match after GroupBy
        completion2.MealId = 99;

        var completions = new List<MealCompletion> { completion1, completion2 };

        var serviceMock = new Mock<IExternalRecipeService>();
        serviceMock
            .Setup(s => s.GetExternalRecipesByURIs(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync([MakeLoaded("Pasta Full", "http://uri/1")]);

        await completions.LoadExternalRecipesAsync(serviceMock.Object);

        // completion2's MealId (99) has no match → assigned new empty Meal
        Assert.That(completions[1].Meal.Id, Is.EqualTo(0));
        Assert.That(completions[1].Meal.Recipes, Is.Empty);
    }

    [Test]
    public async Task LoadExternalRecipesAsync_HandlesMultipleDistinctMeals()
    {
        var meal1 = MakeMeal(1, MakeExternal(10, "Pasta Stub", "http://uri/1"));
        var meal2 = MakeMeal(2, MakeExternal(20, "Soup Stub",  "http://uri/2"));
        var completions = new List<MealCompletion>
        {
            MakeCompletion(meal1, DateTime.Today),
            MakeCompletion(meal2, DateTime.Today),
        };

        var serviceMock = new Mock<IExternalRecipeService>();
        serviceMock
            .Setup(s => s.GetExternalRecipesByURIs(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new List<Recipe>
            {
                MakeLoaded("Pasta Full", "http://uri/1"),
                MakeLoaded("Soup Full",  "http://uri/2"),
            });

        await completions.LoadExternalRecipesAsync(serviceMock.Object);

        Assert.That(completions[0].Meal.Recipes[0].Name, Is.EqualTo("Pasta Full"));
        Assert.That(completions[1].Meal.Recipes[0].Name, Is.EqualTo("Soup Full"));
    }
}
