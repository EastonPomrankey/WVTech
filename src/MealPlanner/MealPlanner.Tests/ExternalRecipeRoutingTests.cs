using MealPlanner.Controllers;
using MealPlanner.DAL.Abstract;
using MealPlanner.Models;
using MealPlanner.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using NUnit.Framework;

namespace MealPlanner.Tests;

[TestFixture]
public class ExternalRecipeRoutingTests
{
    private Mock<IRecipeRepository> _recipeRepo = null!;
    private Mock<IExternalRecipeService> _externalService = null!;
    private Mock<IUserRecipeRepository> _userRecipeRepo = null!;
    private Mock<IRegistrationService> _registrationService = null!;
    private FoodEntriesController _controller = null!;

    [TearDown]
    public void TearDown()
    {
        _controller.Dispose();
    }

    [SetUp]
    public void SetUp()
    {
        _recipeRepo = new Mock<IRecipeRepository>();
        _externalService = new Mock<IExternalRecipeService>();
        _userRecipeRepo = new Mock<IUserRecipeRepository>();
        _registrationService = new Mock<IRegistrationService>();

        _controller = new FoodEntriesController(
            _recipeRepo.Object,
            Mock.Of<ITagRepository>(),
            _userRecipeRepo.Object,
            null!,
            _registrationService.Object,
            Mock.Of<IMeasurementRepository>(),
            Mock.Of<IWebHostEnvironment>(),
            externalRecipeService: _externalService.Object);

        _controller.TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>());

        _userRecipeRepo.Setup(r => r.GetRecipeVotePercentage(It.IsAny<int>())).ReturnsAsync(0f);
        _userRecipeRepo.Setup(r => r.GetUserRecipeVoteAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(UserVoteType.NoVote);
        _userRecipeRepo.Setup(r => r.GetUserOwnedRecipesByUserIdAsync(It.IsAny<string>()))
            .ReturnsAsync([]);
        _registrationService
            .Setup(s => s.FindUserByClaimAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>()))
            .ReturnsAsync((User?)null);
    }

    [Test]
    public async Task Recipes_LocalRecipe_ReturnsSingleRecipeView()
    {
        var recipe = new Recipe { Id = 1, Name = "Pasta", Directions = "Boil water.", ExternalUri = null, Ingredients = [], Tags = [] };
        _recipeRepo.Setup(r => r.ReadRecipeWithIngredientsAsync(1)).ReturnsAsync(recipe);

        var result = await _controller.Recipes(1);

        _externalService.Verify(s => s.GetExternalRecipeByURI(It.IsAny<string>()), Times.Never);
        Assert.That(result, Is.InstanceOf<ViewResult>());
        Assert.That(((ViewResult)result).ViewName, Is.EqualTo("SingleRecipe"));
    }

    [Test]
    public async Task Recipes_EdamamRecipe_CallsEdamamServiceAndReturnsSingleRecipeView()
    {
        var stored = new Recipe { Id = 2, Name = "Chicken", Directions = "", ExternalUri = "edamam:recipe:abc123", Ingredients = [], Tags = [] };
        var fresh = new Recipe { Name = "Chicken", Directions = "", SourceUrl = "https://source.example.com/chicken", Ingredients = [], Tags = [] };

        _recipeRepo.Setup(r => r.ReadRecipeWithIngredientsAsync(2)).ReturnsAsync(stored);
        _externalService.Setup(s => s.GetExternalRecipeByURI("edamam:recipe:abc123")).ReturnsAsync(fresh);

        var result = await _controller.Recipes(2);

        _externalService.Verify(s => s.GetExternalRecipeByURI("edamam:recipe:abc123"), Times.Once);
        Assert.That(result, Is.InstanceOf<ViewResult>());
        var view = (ViewResult)result;
        Assert.That(view.ViewName, Is.EqualTo("SingleRecipe"));
    }

    [Test]
    public async Task Recipes_EdamamRecipe_NoApiService_ReturnsSingleRecipeViewWithStoredData()
    {
        var stored = new Recipe { Id = 7, Name = "Soup", Directions = "", ExternalUri = "edamam:recipe:abc123", Ingredients = [], Tags = [] };
        _recipeRepo.Setup(r => r.ReadRecipeWithIngredientsAsync(7)).ReturnsAsync(stored);

        using var controllerNoApi = new FoodEntriesController(
            _recipeRepo.Object,
            Mock.Of<ITagRepository>(),
            _userRecipeRepo.Object,
            null!,
            _registrationService.Object,
            Mock.Of<IMeasurementRepository>(),
            Mock.Of<IWebHostEnvironment>(),
            externalRecipeService: null);

        var result = await controllerNoApi.Recipes(7);

        Assert.That(result, Is.InstanceOf<ViewResult>());
        Assert.That(((ViewResult)result).ViewName, Is.EqualTo("SingleRecipe"));
    }

    [Test]
    public async Task Recipes_EdamamRecipe_FullOntologyUri_CallsEdamamServiceAndReturnsSingleRecipeView()
    {
        const string ontologyUri = "http://www.edamam.com/ontologies/edamam.owl#recipe_abc123";
        var stored = new Recipe { Id = 6, Name = "Chicken", Directions = "", ExternalUri = ontologyUri, Ingredients = [], Tags = [] };
        var fresh = new Recipe { Name = "Chicken", Directions = "", SourceUrl = "https://source.example.com/chicken", Ingredients = [], Tags = [] };

        _recipeRepo.Setup(r => r.ReadRecipeWithIngredientsAsync(6)).ReturnsAsync(stored);
        _externalService.Setup(s => s.GetExternalRecipeByURI(ontologyUri)).ReturnsAsync(fresh);

        var result = await _controller.Recipes(6);

        _externalService.Verify(s => s.GetExternalRecipeByURI(ontologyUri), Times.Once);
        Assert.That(result, Is.InstanceOf<ViewResult>());
        Assert.That(((ViewResult)result).ViewName, Is.EqualTo("SingleRecipe"));
    }

    [Test]
    public async Task Recipes_NonEdamamExternalWithValidUrl_ReturnsExternalRedirectView()
    {
        const string sourceUrl = "https://www.example.com/tacos";
        var recipe = new Recipe { Id = 3, Name = "Tacos", Directions = "", ExternalUri = sourceUrl, Ingredients = [], Tags = [] };
        _recipeRepo.Setup(r => r.ReadRecipeWithIngredientsAsync(3)).ReturnsAsync(recipe);

        var result = await _controller.Recipes(3);

        _externalService.Verify(s => s.GetExternalRecipeByURI(It.IsAny<string>()), Times.Never);
        Assert.That(result, Is.InstanceOf<ViewResult>());
        var view = (ViewResult)result;
        Assert.That(view.ViewName, Is.EqualTo("ExternalRedirect"));
        Assert.That(view.Model, Is.EqualTo(sourceUrl));
    }

    [Test]
    public async Task Recipes_NonEdamamExternalWithInvalidUrl_RedirectsToSearchWithError()
    {
        var recipe = new Recipe { Id = 4, Name = "Broken", Directions = "", ExternalUri = "not-a-valid-url", Ingredients = [], Tags = [] };
        _recipeRepo.Setup(r => r.ReadRecipeWithIngredientsAsync(4)).ReturnsAsync(recipe);

        var result = await _controller.Recipes(4);

        Assert.That(result, Is.InstanceOf<RedirectToActionResult>());
        Assert.That(((RedirectToActionResult)result).ActionName, Is.EqualTo("SearchRecipes"));
        Assert.That(_controller.TempData["Error"], Is.Not.Null);
    }

    [Test]
    public async Task Recipes_RecipeNotFound_RedirectsToSearch()
    {
        _recipeRepo.Setup(r => r.ReadRecipeWithIngredientsAsync(99)).ReturnsAsync((Recipe?)null);

        var result = await _controller.Recipes(99);

        Assert.That(result, Is.InstanceOf<RedirectToActionResult>());
        Assert.That(((RedirectToActionResult)result).ActionName, Is.EqualTo("SearchRecipes"));
    }

    [Test]
    public async Task Recipes_EdamamRecipeSourceUrlMappedToViewModel()
    {
        const string sourceUrl = "https://www.seriouseats.com/some-recipe";
        var stored = new Recipe { Id = 5, Name = "Soup", Directions = "", ExternalUri = "edamam:recipe:xyz", Ingredients = [], Tags = [] };
        var fresh = new Recipe { Name = "Soup", Directions = "", SourceUrl = sourceUrl, Ingredients = [], Tags = [] };

        _recipeRepo.Setup(r => r.ReadRecipeWithIngredientsAsync(5)).ReturnsAsync(stored);
        _externalService.Setup(s => s.GetExternalRecipeByURI("edamam:recipe:xyz")).ReturnsAsync(fresh);

        var result = await _controller.Recipes(5);

        var view = (ViewResult)result;
        var vm = view.Model as MealPlanner.ViewModels.RecipeViewModel;
        Assert.That(vm, Is.Not.Null);
        Assert.That(vm!.SourceUrl, Is.EqualTo(sourceUrl),
            "SourceUrl from Edamam API response should be mapped through to the ViewModel.");
    }
}
