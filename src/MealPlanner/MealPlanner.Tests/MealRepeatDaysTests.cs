using MealPlanner.Controllers;
using MealPlanner.DAL.Abstract;
using MealPlanner.Helpers;
using MealPlanner.Models;
using MealPlanner.Services;
using MealPlanner.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace MealPlanner.Tests;

[TestFixture]
public class MealRepeatDaysTests
{
    private MealPlannerDBContext _context;
    private SqliteConnection _connection;
    private MealController _controller;
    private Mock<IMealRepository> _mealRepoMock;
    private Mock<IRecipeRepository> _recipeRepoMock;
    private Mock<IRegistrationService> _registrationServiceMock;
    private Mock<ITagRepository> _tagRepoMock;
    private Mock<IShoppingListService> _shoppingListServiceMock;
    private ClaimsPrincipal _user;
    private User _testUser;

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<MealPlannerDBContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new MealPlannerDBContext(options);
        _context.Database.EnsureCreated();

        _testUser = new User { Id = "user-1", FullName = "testuser" };

        _user = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "user-1"),
                new Claim(ClaimTypes.Name, "testuser")
            },
            "TestAuth"));

        _registrationServiceMock = new Mock<IRegistrationService>();
        _registrationServiceMock
            .Setup(r => r.FindUserByClaimAsync(_user))
            .ReturnsAsync(_testUser);

        _recipeRepoMock = new Mock<IRecipeRepository>();
        _mealRepoMock = new Mock<IMealRepository>();
        _tagRepoMock = new Mock<ITagRepository>();

        var httpContext = new DefaultHttpContext { User = _user };

        _shoppingListServiceMock = new Mock<IShoppingListService>();

        _controller = new MealController(
            _registrationServiceMock.Object,
            _recipeRepoMock.Object,
            _mealRepoMock.Object,
            _context,
            _tagRepoMock.Object,
            _shoppingListServiceMock.Object);

        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        _controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
    }

    [TearDown]
    public void TearDown()
    {
        _controller.Dispose();
        _context.Dispose();
        _connection.Dispose();
    }

    // ── MealSchedule.MealsForDate ────────────────────────────────────────────

    private static Meal MakeWeeklyMeal(DayOfWeek startDay, string? repeatDays)
    {
        // Pin StartTime to a known date with the right DayOfWeek (2026-04-27 = Monday)
        var anchor = new DateTime(2026, 4, 27); // Monday
        var offset = ((int)startDay - (int)DayOfWeek.Monday + 7) % 7;
        return new Meal
        {
            Id = 1,
            UserId = "user-1",
            Title = "Test",
            StartTime = anchor.AddDays(offset),
            RepeatRule = "Weekly",
            RepeatDays = repeatDays
        };
    }

    [Test]
    public void MealsForDate_RepeatDaysContainsQueriedDay_ReturnsMeal()
    {
        var meal = MakeWeeklyMeal(DayOfWeek.Monday, "1,3"); // Mon+Wed
        var wednesday = new DateTime(2026, 4, 29); // Wednesday

        var result = MealSchedule.MealsForDate(new[] { meal }, wednesday);

        Assert.That(result, Has.Count.EqualTo(1));
    }

    [Test]
    public void MealsForDate_RepeatDaysDoesNotContainQueriedDay_DoesNotReturnMeal()
    {
        var meal = MakeWeeklyMeal(DayOfWeek.Monday, "1,3"); // Mon+Wed
        var thursday = new DateTime(2026, 4, 30); // Thursday

        var result = MealSchedule.MealsForDate(new[] { meal }, thursday);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void MealsForDate_RepeatDaysNull_FallsBackToStartTimeDayOfWeek()
    {
        // Old meal: RepeatRule="Weekly", no RepeatDays — must appear on its StartTime's DayOfWeek
        var meal = MakeWeeklyMeal(DayOfWeek.Monday, null);
        var nextMonday = new DateTime(2026, 5, 4); // Monday

        var result = MealSchedule.MealsForDate(new[] { meal }, nextMonday);

        Assert.That(result, Has.Count.EqualTo(1));
    }

    [Test]
    public void MealsForDate_NoRepeatRule_OnlyAppearsOnExactDate()
    {
        var exactDate = new DateTime(2026, 4, 27);
        var meal = new Meal
        {
            Id = 1, UserId = "user-1", Title = "Test",
            StartTime = exactDate,
            RepeatRule = null
        };

        var result = MealSchedule.MealsForDate(new[] { meal }, exactDate.AddDays(7));

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void MealsForDate_MultipleDaysSelected_AppearsOnAllOfThem()
    {
        var meal = MakeWeeklyMeal(DayOfWeek.Monday, "1,2,4"); // Mon, Tue, Thu

        var monday    = new DateTime(2026, 4, 27);
        var tuesday   = new DateTime(2026, 4, 28);
        var thursday  = new DateTime(2026, 4, 30);
        var wednesday = new DateTime(2026, 4, 29); // not selected

        Assert.Multiple(() =>
        {
            Assert.That(MealSchedule.MealsForDate(new[] { meal }, monday),    Has.Count.EqualTo(1));
            Assert.That(MealSchedule.MealsForDate(new[] { meal }, tuesday),   Has.Count.EqualTo(1));
            Assert.That(MealSchedule.MealsForDate(new[] { meal }, thursday),  Has.Count.EqualTo(1));
            Assert.That(MealSchedule.MealsForDate(new[] { meal }, wednesday), Is.Empty);
        });
    }

    // ── MealRepository.GetUserMealsByDateAsync ───────────────────────────────

    private async Task<MealPlanner.DAL.Concrete.MealRepository> MakeRepoWithMeal(
        string? repeatDays, DayOfWeek startDay)
    {
        var anchor = new DateTime(2026, 4, 27); // Monday
        var offset = ((int)startDay - (int)DayOfWeek.Monday + 7) % 7;

        _context.Users.Add(_testUser);
        _context.Meals.Add(new Meal
        {
            UserId = _testUser.Id,
            Title = "Repo Meal",
            StartTime = anchor.AddDays(offset),
            RepeatRule = "Weekly",
            RepeatDays = repeatDays
        });
        await _context.SaveChangesAsync();

        return new MealPlanner.DAL.Concrete.MealRepository(_context);
    }

    [Test]
    public async Task GetUserMealsByDateAsync_RepeatDaysContainsDate_ReturnsMeal()
    {
        var repo = await MakeRepoWithMeal("1,3", DayOfWeek.Monday); // Mon+Wed
        var wednesday = new DateTime(2026, 4, 29);

        var result = await repo.GetUserMealsByDateAsync(_testUser, wednesday);

        Assert.That(result, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetUserMealsByDateAsync_RepeatDaysDoesNotContainDate_DoesNotReturnMeal()
    {
        var repo = await MakeRepoWithMeal("1,3", DayOfWeek.Monday); // Mon+Wed
        var thursday = new DateTime(2026, 4, 30);

        var result = await repo.GetUserMealsByDateAsync(_testUser, thursday);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetUserMealsByDateRangeAsync_RepeatDaysSpanRange_ReturnsCorrectMeals()
    {
        var repo = await MakeRepoWithMeal("1,4", DayOfWeek.Monday); // Mon+Thu
        var monday = new DateTime(2026, 4, 27);
        var sunday = new DateTime(2026, 5, 3);

        var result = await repo.GetUserMealsByDateRangeAsync(_testUser, monday, sunday);

        // Single distinct meal record, but it matches both Mon and Thu in the range
        Assert.That(result, Has.Count.EqualTo(1));
    }

    // ── MealController.NewMeal POST ──────────────────────────────────────────

    [Test]
    public async Task NewMeal_Post_WithRepeatDays_SavesMealWithCorrectDays()
    {
        var model = new CreateMealViewModel
        {
            Title = "Test Meal",
            SelectedMonth = DateTime.Today.Month,
            SelectedDay = DateTime.Today.Day,
            RepeatWeekly = true,
            RepeatDays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Wednesday }
        };

        Meal? savedMeal = null;
        _mealRepoMock
            .Setup(r => r.CreateOrUpdate(It.IsAny<Meal>()))
            .Callback<Meal>(m => savedMeal = m)
            .Returns<Meal>(m => m);

        await _controller.NewMeal(model);

        Assert.That(savedMeal, Is.Not.Null);
        Assert.That(savedMeal!.RepeatRule, Is.EqualTo("Weekly"));
        var days = MealSchedule.ParseRepeatDays(savedMeal.RepeatDays).ToList();
        Assert.That(days, Contains.Item(DayOfWeek.Monday));
        Assert.That(days, Contains.Item(DayOfWeek.Wednesday));
    }

    [Test]
    public async Task NewMeal_Post_RepeatWeeklyFalse_RepeatRuleAndDaysAreNull()
    {
        var model = new CreateMealViewModel
        {
            Title = "Test Meal",
            SelectedMonth = DateTime.Today.Month,
            SelectedDay = DateTime.Today.Day,
            RepeatWeekly = false,
            RepeatDays = new List<DayOfWeek> { DayOfWeek.Monday }
        };

        Meal? savedMeal = null;
        _mealRepoMock
            .Setup(r => r.CreateOrUpdate(It.IsAny<Meal>()))
            .Callback<Meal>(m => savedMeal = m)
            .Returns<Meal>(m => m);

        await _controller.NewMeal(model);

        Assert.That(savedMeal, Is.Not.Null);
        Assert.That(savedMeal!.RepeatRule, Is.Null);
        Assert.That(savedMeal.RepeatDays, Is.Null);
    }

    // ── MealController.EditMeal GET ──────────────────────────────────────────

    [Test]
    public async Task EditMeal_Get_MapsRepeatDaysToViewModel()
    {
        _context.Users.Add(_testUser);
        var meal = new Meal
        {
            UserId = _testUser.Id,
            Title = "Repeat Meal",
            StartTime = new DateTime(2026, 4, 27),
            RepeatRule = "Weekly",
            RepeatDays = "1,3" // Mon+Wed
        };
        _context.Meals.Add(meal);
        await _context.SaveChangesAsync();

        _mealRepoMock
            .Setup(r => r.ReadAsync(meal.Id))
            .ReturnsAsync(meal);
        _mealRepoMock
            .Setup(r => r.LoadRecipesAsync(It.IsAny<Meal>()))
            .Returns(Task.CompletedTask);

        var result = await _controller.EditMeal(meal.Id) as ViewResult;
        var vm = result?.Model as EditMealViewModel;

        Assert.That(vm, Is.Not.Null);
        Assert.That(vm!.RepeatDays, Contains.Item(DayOfWeek.Monday));
        Assert.That(vm.RepeatDays, Contains.Item(DayOfWeek.Wednesday));
        Assert.That(vm.RepeatDays, Does.Not.Contain(DayOfWeek.Tuesday));
    }

    // ── MealController.EditMeal POST ─────────────────────────────────────────

    [Test]
    public async Task EditMeal_Post_ChangedRepeatDays_UpdatesMeal()
    {
        _context.Users.Add(_testUser);
        var meal = new Meal
        {
            UserId = _testUser.Id,
            Title = "Morning Meal",
            StartTime = new DateTime(2026, 4, 27),
            RepeatRule = "Weekly",
            RepeatDays = "1,3" // Mon+Wed
        };
        _context.Meals.Add(meal);
        await _context.SaveChangesAsync();

        _mealRepoMock.Setup(r => r.Read(meal.Id)).Returns(meal);
        _mealRepoMock.Setup(r => r.LoadRecipesAsync(meal)).Returns(Task.CompletedTask);

        var model = new EditMealViewModel
        {
            Id = meal.Id,
            Title = "Morning Meal",
            SelectedMonth = 4,
            SelectedDay = 27,
            RepeatWeekly = true,
            RepeatDays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Friday }
        };

        await _controller.EditMeal(model);

        var days = MealSchedule.ParseRepeatDays(meal.RepeatDays).ToList();
        Assert.That(days, Contains.Item(DayOfWeek.Monday));
        Assert.That(days, Contains.Item(DayOfWeek.Friday));
        Assert.That(days, Does.Not.Contain(DayOfWeek.Wednesday));
    }
}
