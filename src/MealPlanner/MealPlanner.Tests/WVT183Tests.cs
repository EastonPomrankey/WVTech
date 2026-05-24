using MealPlanner.DAL.Abstract;
using MealPlanner.DAL.Concrete;
using MealPlanner.Models;
using MealPlanner.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;

namespace MealPlanner.Tests;

[TestFixture]
public class WVT183ShoppingListSyncTests
{
    private MealPlannerDBContext _context = null!;
    private ShoppingListService _service = null!;
    private Measurement _tsp = null!;
    private Measurement _tbsp = null!;
    private Measurement _cup = null!;
    private readonly string _userId = "sync-user";

    [SetUp]
    public void SetUp()
    {
        var connection = new SqliteConnection("Filename=:memory:");
        connection.Open();
        _context = new MealPlannerDBContext(
            new DbContextOptionsBuilder<MealPlannerDBContext>().UseSqlite(connection).Options);
        _context.Database.EnsureCreated();

        _tsp  = new Measurement { Name = "Teaspoon",   Abbreviation = "tsp",  SortOrder = 3 };
        _tbsp = new Measurement { Name = "Tablespoon", Abbreviation = "tbsp", SortOrder = 4 };
        _cup  = new Measurement { Name = "Cup",        Abbreviation = "cup",  SortOrder = 6 };
        _context.Set<Measurement>().AddRange(_tsp, _tbsp, _cup);
        _context.SaveChanges();

        _context.MeasurementConversions.AddRange(
            new MeasurementConversion { FromMeasurementId = _tsp.Id,  ToMeasurementId = _tsp.Id, Factor = 1f },
            new MeasurementConversion { FromMeasurementId = _tbsp.Id, ToMeasurementId = _tsp.Id, Factor = 3f },
            new MeasurementConversion { FromMeasurementId = _cup.Id,  ToMeasurementId = _tsp.Id, Factor = 48f }
        );
        _context.SaveChanges();

        _service = new ShoppingListService(
            new ShoppingListRepository(_context),
            Mock.Of<IMealRepository>(),
            Mock.Of<IIngredientBaseRepository>(),
            new MeasurementRepository(_context),
            new MeasurementConversionRepository(_context));
    }

    [TearDown]
    public void TearDown() => _context.Dispose();

    private (IngredientBase ib, Ingredient ingredient) MakeIngredient(string name, float amount, Measurement measurement)
    {
        var ib = _context.Set<IngredientBase>().FirstOrDefault(b => b.Name == name)
            ?? _context.Set<IngredientBase>().Add(new IngredientBase { Name = name }).Entity;
        _context.SaveChanges();
        return (ib, new Ingredient { IngredientBase = ib, Measurement = measurement, Amount = amount, DisplayName = name });
    }

    [Test]
    public void SyncFromDateRange_SameUnitSameIngredient_MergesQuantity()
    {
        var (_, i1) = MakeIngredient("wvt183sugar", 3f, _tsp);
        var (_, i2) = MakeIngredient("wvt183sugar", 2f, _tsp);

        var mealRepoMock = new Mock<IMealRepository>();
        var user = new User { Id = _userId };
        _context.Users.Add(user);
        _context.SaveChanges();

        var recipe1 = new Recipe { Name = "r1", Directions = "", Ingredients = [i1] };
        var recipe2 = new Recipe { Name = "r2", Directions = "", Ingredients = [i2] };
        var meal = new Meal { UserId = _userId, Title = "m1", StartTime = DateTime.Today, Recipes = [recipe1, recipe2] };
        _context.Add(meal);
        _context.SaveChanges();

        mealRepoMock.Setup(r => r.GetUserMealsByDateRangeWithIngredientsAsync(user, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync([meal]);

        var svc = new ShoppingListService(
            new ShoppingListRepository(_context),
            mealRepoMock.Object,
            Mock.Of<IIngredientBaseRepository>(),
            new MeasurementRepository(_context),
            new MeasurementConversionRepository(_context));

        svc.SyncFromDateRangeAsync(_userId, user, DateTime.Today, DateTime.Today).Wait();

        var items = _context.ShoppingListItems.Where(i => i.UserId == _userId).ToList();
        Assert.That(items.Count, Is.EqualTo(1));
        Assert.That(items[0].Amount, Is.EqualTo(5f).Within(0.01f));
        Assert.That(items[0].MeasurementId, Is.EqualTo(_tsp.Id));
    }

    [Test]
    public void SyncFromDateRange_TspAndTbsp_ConvertsToTspAndMerges()
    {
        var (_, i1) = MakeIngredient("wvt183sugar", 3f, _tsp);
        var (_, i2) = MakeIngredient("wvt183sugar", 1f, _tbsp);

        var user = new User { Id = _userId };
        _context.Users.Add(user);
        _context.SaveChanges();

        var recipe1 = new Recipe { Name = "r3", Directions = "", Ingredients = [i1] };
        var recipe2 = new Recipe { Name = "r4", Directions = "", Ingredients = [i2] };
        var meal = new Meal { UserId = _userId, Title = "m2", StartTime = DateTime.Today, Recipes = [recipe1, recipe2] };
        _context.Add(meal);
        _context.SaveChanges();

        var mealRepoMock = new Mock<IMealRepository>();
        mealRepoMock.Setup(r => r.GetUserMealsByDateRangeWithIngredientsAsync(user, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync([meal]);

        var svc = new ShoppingListService(
            new ShoppingListRepository(_context),
            mealRepoMock.Object,
            Mock.Of<IIngredientBaseRepository>(),
            new MeasurementRepository(_context),
            new MeasurementConversionRepository(_context));

        svc.SyncFromDateRangeAsync(_userId, user, DateTime.Today, DateTime.Today).Wait();

        var items = _context.ShoppingListItems.Where(i => i.UserId == _userId).ToList();
        Assert.That(items.Count, Is.EqualTo(1), "3 tsp + 1 tbsp should merge into one item");
        Assert.That(items[0].Amount, Is.EqualTo(6f).Within(0.01f), "3 tsp + (1 tbsp * 3) = 6 tsp");
        Assert.That(items[0].MeasurementId, Is.EqualTo(_tsp.Id), "tsp+tbsp both convert to tsp base unit");
    }

    [Test]
    public void SyncFromDateRange_CapitalizationDifference_MergesIngredients()
    {
        var ibLower = _context.Set<IngredientBase>().Add(new IngredientBase { Name = "chicken breast" }).Entity;
        var ibUpper = _context.Set<IngredientBase>().Add(new IngredientBase { Name = "Chicken Breast" }).Entity;
        _context.SaveChanges();

        var i1 = new Ingredient { IngredientBase = ibUpper, Measurement = _cup, Amount = 2f, DisplayName = "Chicken Breast" };
        var i2 = new Ingredient { IngredientBase = ibLower, Measurement = _cup, Amount = 3f, DisplayName = "chicken breast" };

        var user = new User { Id = _userId };
        _context.Users.Add(user);
        _context.SaveChanges();

        var recipe1 = new Recipe { Name = "r5", Directions = "", Ingredients = [i1] };
        var recipe2 = new Recipe { Name = "r6", Directions = "", Ingredients = [i2] };
        var meal = new Meal { UserId = _userId, Title = "m3", StartTime = DateTime.Today, Recipes = [recipe1, recipe2] };
        _context.Add(meal);
        _context.SaveChanges();

        var mealRepoMock = new Mock<IMealRepository>();
        mealRepoMock.Setup(r => r.GetUserMealsByDateRangeWithIngredientsAsync(user, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync([meal]);

        var svc = new ShoppingListService(
            new ShoppingListRepository(_context),
            mealRepoMock.Object,
            Mock.Of<IIngredientBaseRepository>(),
            new MeasurementRepository(_context),
            new MeasurementConversionRepository(_context));

        svc.SyncFromDateRangeAsync(_userId, user, DateTime.Today, DateTime.Today).Wait();

        var items = _context.ShoppingListItems.Where(i => i.UserId == _userId).ToList();
        Assert.That(items.Count, Is.EqualTo(1), "Chicken Breast and chicken breast should merge");
        Assert.That(items[0].Amount, Is.EqualTo(5f).Within(0.01f), "2 cups + 3 cups = 5 cups (original unit preserved)");
    }

    [Test]
    public void FindConflictingItems_IncompatibleUnit_ReturnsConflict()
    {
        var count = new Measurement { Name = "Count", Abbreviation = "Count", SortOrder = 1 };
        _context.Set<Measurement>().Add(count);
        _context.SaveChanges();

        var ib = _context.Set<IngredientBase>().Add(new IngredientBase { Name = "wvt183garlic" }).Entity;
        _context.SaveChanges();

        _context.ShoppingListItems.Add(new ShoppingListItem
        {
            UserId = _userId,
            IngredientBase = ib,
            Measurement = count,
            Amount = 12f,
            IsAutoAdded = false
        });
        _context.SaveChanges();

        var conflicts = _service.FindConflictingItems(_userId, "wvt183garlic", "Tablespoon").ToList();

        Assert.That(conflicts, Has.Count.EqualTo(1), "Count and Tablespoon are incompatible — should flag a conflict");
        Assert.That(conflicts[0].MeasurementId, Is.EqualTo(count.Id));
    }

    [Test]
    public void FindAutoAddedConflicts_AutoAddedHasIncompatibleUnit_ReturnsConflict()
    {
        var count = new Measurement { Name = "Count", Abbreviation = "Count", SortOrder = 1 };
        _context.Set<Measurement>().Add(count);
        _context.SaveChanges();

        var ib = _context.Set<IngredientBase>().Add(new IngredientBase { Name = "wvt183garlic" }).Entity;
        _context.SaveChanges();

        _context.ShoppingListItems.AddRange(
            new ShoppingListItem { UserId = _userId, IngredientBase = ib, Measurement = count, Amount = 12f, IsAutoAdded = false },
            new ShoppingListItem { UserId = _userId, IngredientBase = ib, Measurement = _tsp,  Amount = 3f,  IsAutoAdded = true }
        );
        _context.SaveChanges();

        var conflicts = _service.FindAutoAddedConflicts(_userId).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(conflicts, Has.Count.EqualTo(1));
            Assert.That(conflicts[0].AutoAdded.IsAutoAdded, Is.True);
            Assert.That(conflicts[0].Manual.IsAutoAdded, Is.False);
        });
    }

    [Test]
    public void FindAutoAddedConflicts_SameFamily_ReturnsNoConflict()
    {
        var ib = _context.Set<IngredientBase>().Add(new IngredientBase { Name = "wvt183milk" }).Entity;
        _context.SaveChanges();

        _context.ShoppingListItems.AddRange(
            new ShoppingListItem { UserId = _userId, IngredientBase = ib, Measurement = _cup,  Amount = 1f, IsAutoAdded = false },
            new ShoppingListItem { UserId = _userId, IngredientBase = ib, Measurement = _tbsp, Amount = 2f, IsAutoAdded = true }
        );
        _context.SaveChanges();

        var conflicts = _service.FindAutoAddedConflicts(_userId).ToList();

        Assert.That(conflicts, Has.Count.EqualTo(0), "cup and tbsp both convert to tsp — no conflict");
    }

    [Test]
    public void FindAutoAddedConflicts_BothAutoIncompatibleUnits_ReturnsConflict()
    {
        var count = new Measurement { Name = "Count", Abbreviation = "Count", SortOrder = 1 };
        _context.Set<Measurement>().Add(count);
        _context.SaveChanges();

        var ib = _context.Set<IngredientBase>().Add(new IngredientBase { Name = "wvt183pepper" }).Entity;
        _context.SaveChanges();

        _context.ShoppingListItems.AddRange(
            new ShoppingListItem { UserId = _userId, IngredientBase = ib, Measurement = _tbsp, Amount = 1f, IsAutoAdded = true },
            new ShoppingListItem { UserId = _userId, IngredientBase = ib, Measurement = count,  Amount = 1f, IsAutoAdded = true }
        );
        _context.SaveChanges();

        var conflicts = _service.FindAutoAddedConflicts(_userId).ToList();

        Assert.That(conflicts, Has.Count.EqualTo(1), "tbsp and count are incompatible — conflict should be reported even when both are auto-added");
    }

    [Test]
    public void FindConflictingItems_SameFamily_ReturnsNoConflict()
    {
        var ib = _context.Set<IngredientBase>().Add(new IngredientBase { Name = "wvt183water" }).Entity;
        _context.SaveChanges();

        _context.ShoppingListItems.Add(new ShoppingListItem
        {
            UserId = _userId,
            IngredientBase = ib,
            Measurement = _cup,
            Amount = 1f,
            IsAutoAdded = false
        });
        _context.SaveChanges();

        var conflicts = _service.FindConflictingItems(_userId, "wvt183water", "Tablespoon").ToList();

        Assert.That(conflicts, Has.Count.EqualTo(0), "Cup and Tablespoon both convert to tsp — no conflict");
    }

    [Test]
    public void SyncFromDateRange_NoConversionForMeasurement_GroupsByOriginalUnit()
    {
        var pinch = new Measurement { Name = "Pinch", Abbreviation = "Pinch", SortOrder = 2 };
        _context.Set<Measurement>().Add(pinch);
        _context.SaveChanges();

        var (_, i1) = MakeIngredient("wvt183salt", 1f, pinch);
        var (_, i2) = MakeIngredient("wvt183salt", 2f, pinch);

        var user = new User { Id = _userId };
        _context.Users.Add(user);
        _context.SaveChanges();

        var recipe1 = new Recipe { Name = "r7", Directions = "", Ingredients = [i1] };
        var recipe2 = new Recipe { Name = "r8", Directions = "", Ingredients = [i2] };
        var meal = new Meal { UserId = _userId, Title = "m4", StartTime = DateTime.Today, Recipes = [recipe1, recipe2] };
        _context.Add(meal);
        _context.SaveChanges();

        var mealRepoMock = new Mock<IMealRepository>();
        mealRepoMock.Setup(r => r.GetUserMealsByDateRangeWithIngredientsAsync(user, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync([meal]);

        var svc = new ShoppingListService(
            new ShoppingListRepository(_context),
            mealRepoMock.Object,
            Mock.Of<IIngredientBaseRepository>(),
            new MeasurementRepository(_context),
            new MeasurementConversionRepository(_context));

        svc.SyncFromDateRangeAsync(_userId, user, DateTime.Today, DateTime.Today).Wait();

        var items = _context.ShoppingListItems.Where(i => i.UserId == _userId).ToList();
        Assert.That(items.Count, Is.EqualTo(1));
        Assert.That(items[0].Amount, Is.EqualTo(3f).Within(0.01f));
        Assert.That(items[0].MeasurementId, Is.EqualTo(pinch.Id));
    }

    [Test]
    public void AllowAll_SubsequentSyncKeepsBothItems_AutoItemIsNotDismissed()
    {
        // "Allow all" stores accepted pairs in a cookie (controller concern) and leaves
        // the auto-added item as IsAutoAdded=true so the next sync recreates it naturally.
        // This test verifies the service-layer invariant: an accepted conflict is not dismissed,
        // so the auto-added item reappears on every sync while the recipe exists.
        var count = new Measurement { Name = "Count", Abbreviation = "Count", SortOrder = 1 };
        _context.Set<Measurement>().Add(count);
        _context.SaveChanges();

        var ib = _context.Set<IngredientBase>().Add(new IngredientBase { Name = "wvt183onion" }).Entity;
        _context.SaveChanges();

        var user = new User { Id = _userId };
        _context.Users.Add(user);
        _context.SaveChanges();

        _context.ShoppingListItems.Add(new ShoppingListItem
        {
            UserId = _userId, IngredientBase = ib, Measurement = count, Amount = 3f, IsAutoAdded = false
        });
        _context.SaveChanges();

        var i1 = new Ingredient { IngredientBase = ib, Measurement = _tbsp, Amount = 2f, DisplayName = "wvt183onion" };
        var recipe = new Recipe { Name = "r-onion", Directions = "", Ingredients = [i1] };
        var meal = new Meal { UserId = _userId, Title = "onion-meal", StartTime = DateTime.Today, Recipes = [recipe] };
        _context.Add(meal);
        _context.SaveChanges();

        var mealRepoMock = new Mock<IMealRepository>();
        mealRepoMock.Setup(r => r.GetUserMealsByDateRangeWithIngredientsAsync(user, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync([meal]);

        var svc = new ShoppingListService(
            new ShoppingListRepository(_context),
            mealRepoMock.Object,
            Mock.Of<IIngredientBaseRepository>(),
            new MeasurementRepository(_context),
            new MeasurementConversionRepository(_context));

        // First sync: conflict auto-added
        svc.SyncFromDateRangeAsync(_userId, user, DateTime.Today, DateTime.Today).Wait();
        var afterFirst = _context.ShoppingListItems.Where(i => i.UserId == _userId).ToList();
        Assert.That(afterFirst.Count, Is.EqualTo(2), "Manual and auto item should both be present after first sync");

        // Second sync: auto item is NOT dismissed so it is re-added — both items persist
        svc.SyncFromDateRangeAsync(_userId, user, DateTime.Today, DateTime.Today).Wait();
        var afterSecond = _context.ShoppingListItems.Where(i => i.UserId == _userId).ToList();

        Assert.That(afterSecond.Count, Is.EqualTo(2), "Both items should persist across syncs when conflict is accepted (not dismissed)");
        Assert.That(afterSecond.Any(i => !i.IsAutoAdded && i.MeasurementId == count.Id), Is.True, "Manual item should remain");
        Assert.That(afterSecond.Any(i => i.IsAutoAdded && i.MeasurementId == _tbsp.Id), Is.True, "Auto item should be re-added by sync");

        var dismissed = _context.DismissedShoppingItems.Any(d => d.UserId == _userId && d.IngredientBaseId == ib.Id);
        Assert.That(dismissed, Is.False, "Ingredient base must NOT be dismissed — dismiss would prevent the auto item from reappearing");
    }

    [Test]
    public void AddItem_EmptyMeasurement_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            _service.AddItem(_userId, "wvt183flour", 1f, ""));
    }

    [Test]
    public void AddItem_WhitespaceMeasurement_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            _service.AddItem(_userId, "wvt183flour", 1f, "   "));
    }

    [Test]
    public void AddItem_UnknownMeasurementName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            _service.AddItem(_userId, "wvt183flour", 1f, "hectoliter"));
    }

    [Test]
    public void ResolveAutoAddedConflicts_Decline_RemovesAutoItemAndDismisses()
    {
        var count = new Measurement { Name = "Count", Abbreviation = "Count", SortOrder = 1 };
        _context.Set<Measurement>().Add(count);
        _context.SaveChanges();

        var ib = _context.Set<IngredientBase>().Add(new IngredientBase { Name = "wvt183basil" }).Entity;
        _context.SaveChanges();

        var user = new User { Id = _userId };
        _context.Users.Add(user);
        _context.SaveChanges();

        var manual = new ShoppingListItem { UserId = _userId, IngredientBase = ib, Measurement = count, Amount = 5f, IsAutoAdded = false };
        var auto   = new ShoppingListItem { UserId = _userId, IngredientBase = ib, Measurement = _cup,  Amount = 1f, IsAutoAdded = true };
        _context.ShoppingListItems.AddRange(manual, auto);
        _context.SaveChanges();

        _service.ResolveAutoAddedConflicts(_userId, [auto.Id]);

        var autoRemoved = _context.ShoppingListItems.Find(auto.Id);
        Assert.That(autoRemoved, Is.Null, "Auto item should be removed after declining");

        var manualStays = _context.ShoppingListItems.Find(manual.Id);
        Assert.That(manualStays, Is.Not.Null, "Original manual item should remain after declining");
        Assert.That(manualStays!.Amount, Is.EqualTo(5f), "Original manual item amount should be unchanged");

        var dismissed = _context.DismissedShoppingItems.Any(d => d.UserId == _userId && d.IngredientBaseId == ib.Id);
        Assert.That(dismissed, Is.True, "Declined ingredient base should be dismissed so it doesn't reappear from sync");
    }
}
