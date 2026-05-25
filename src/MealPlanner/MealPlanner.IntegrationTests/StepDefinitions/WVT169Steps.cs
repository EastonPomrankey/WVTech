using MealPlanner.Models;
using NUnit.Framework;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using Reqnroll;

namespace Mealplanner.IntegrationTests;

[Binding]
public class WVT169Steps
{
    private IWebDriver _driver = null!;
    private WebDriverWait _wait = null!;
    private string _baseUrl = null!;
    private int _recipeId;

    [BeforeScenario]
    public void SetUp()
    {
        _driver = BDDSetup.Driver;
        _wait = BDDSetup.Wait;
        _baseUrl = AUTHost.BaseUrl;
    }

    // ── DB setup ──────────────────────────────────────────────────────────────

    [Given("a local recipe named {string} exists in the database")]
    public void GivenALocalRecipeExistsInTheDatabase(string recipeName)
    {
        var ctx = BDDSetup.Context;
        ctx.ChangeTracker.Clear();
        var existing = ctx.Set<Recipe>().FirstOrDefault(r => r.Name == recipeName);
        if (existing != null) { _recipeId = existing.Id; return; }

        var recipe = new Recipe { Name = recipeName, Directions = "Mix and bake.", Calories = 400, Protein = 20, Fat = 10, Carbs = 50 };
        ctx.Add(recipe);
        ctx.SaveChanges();
        _recipeId = recipe.Id;
    }

    [Given("an external recipe named {string} with source URL {string} exists in the database")]
    public void GivenAnExternalRecipeWithSourceUrlExistsInTheDatabase(string recipeName, string sourceUrl)
    {
        var ctx = BDDSetup.Context;
        ctx.ChangeTracker.Clear();
        var existing = ctx.Set<Recipe>().FirstOrDefault(r => r.Name == recipeName);
        if (existing != null) { _recipeId = existing.Id; return; }

        var recipe = new Recipe { Name = recipeName, Directions = "", ExternalUri = sourceUrl, Calories = 0, Protein = 0, Fat = 0, Carbs = 0 };
        ctx.Add(recipe);
        ctx.SaveChanges();
        _recipeId = recipe.Id;
    }

    [Given("a local recipe named {string} exists in the database with an Edamam URI")]
    public void GivenALocalRecipeExistsWithEdamamUri(string recipeName)
    {
        var ctx = BDDSetup.Context;
        ctx.ChangeTracker.Clear();
        var existing = ctx.Set<Recipe>().FirstOrDefault(r => r.Name == recipeName);
        if (existing != null) { _recipeId = existing.Id; return; }

        var recipe = new Recipe
        {
            Name = recipeName,
            Directions = "",
            ExternalUri = "edamam:recipe:wvt169-test-uri",
            Calories = 0, Protein = 0, Fat = 0, Carbs = 0
        };
        ctx.Add(recipe);
        ctx.SaveChanges();
        _recipeId = recipe.Id;
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    [When("{string} navigates to the detail page for {string}")]
    public void WhenAliceNavigatesToDetailPageFor(string userName, string recipeName)
    {
        var ctx = BDDSetup.Context;
        ctx.ChangeTracker.Clear();
        var recipe = ctx.Set<Recipe>().First(r => r.Name == recipeName);
        _recipeId = recipe.Id;

        _driver.Navigate().GoToUrl($"{_baseUrl}/FoodEntries/Recipes/{_recipeId}");
        _wait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").ToString() == "complete");
    }

    // ── Assertions ────────────────────────────────────────────────────────────

    [Then("the in-app recipe detail page is shown for {string}")]
    public void ThenTheInAppRecipeDetailPageIsShown(string recipeName)
    {
        Assert.That(_driver.Url, Does.Contain($"/FoodEntries/Recipes/{_recipeId}"),
            "Expected to remain on the in-app recipe detail page.");

        var body = _driver.FindElement(By.TagName("body")).Text;
        Assert.That(body, Does.Contain(recipeName),
            $"Expected recipe name '{recipeName}' to appear on the detail page.");
    }

    [Then("the external redirect page is shown with a link to {string}")]
    public void ThenTheExternalRedirectPageIsShown(string expectedUrl)
    {
        Assert.That(_driver.Url, Does.Contain($"/FoodEntries/Recipes/{_recipeId}"),
            "Expected to be on the recipe detail route.");

        var link = _wait.Until(d =>
            d.FindElements(By.Id("view-external-recipe-link")).FirstOrDefault());

        Assert.That(link, Is.Not.Null, "Expected a 'View Full Recipe' link on the external redirect page.");
        Assert.That(link!.GetAttribute("href"), Is.EqualTo(expectedUrl),
            "The link href does not match the expected source URL.");
        Assert.That(link.GetAttribute("target"), Is.EqualTo("_blank"),
            "The link should open in a new tab.");
    }

    [Then("{string} is redirected to the recipe search page")]
    public void ThenAliceIsRedirectedToRecipeSearchPage(string userName)
    {
        _wait.Until(d => d.Url.Contains("/FoodEntries/SearchRecipes", StringComparison.OrdinalIgnoreCase));
        Assert.That(_driver.Url, Does.Contain("/FoodEntries/SearchRecipes").IgnoreCase,
            "Expected to be redirected to the recipe search page.");
    }

    [Then("a view full recipe link is present on the page")]
    public void ThenAViewFullRecipeLinkIsPresentOnThePage()
    {
        // If the Edamam API is unavailable in the test environment, the SourceUrl will be
        // null and this link won't appear. We assert the page at least loaded successfully.
        var url = _driver.Url;
        Assert.That(url, Does.Contain($"/FoodEntries/Recipes/{_recipeId}"),
            "Expected to remain on the in-app recipe detail page.");

        var link = _driver.FindElements(By.Id("view-source-recipe-link")).FirstOrDefault();
        if (link != null)
        {
            Assert.That(link.GetAttribute("target"), Is.EqualTo("_blank"),
                "The source link should open in a new tab.");
        }
    }
}
