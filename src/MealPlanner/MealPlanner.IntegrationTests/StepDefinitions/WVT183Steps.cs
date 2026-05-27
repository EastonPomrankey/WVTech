using System.Globalization;
using MealPlanner.Models;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using Reqnroll;

namespace Mealplanner.IntegrationTests;

[Binding]
public class WVT183Steps
{
    private readonly IWebDriver _driver;
    private readonly WebDriverWait _wait;
    private readonly string _baseUrl;
    private readonly string _emailBase = "@fakeemail.com";

    public WVT183Steps()
    {
        _driver = BDDSetup.Driver;
        _wait = BDDSetup.Wait;
        _baseUrl = AUTHost.BaseUrl;
    }

    [Given("{string} has a meal with ingredient {string} in {string} with amount {string} for today")]
    public void GivenUserHasMealWithIngredientForToday(string userName, string ingredientName, string measurement, string amountStr)
    {
        float amount = float.Parse(amountStr, CultureInfo.InvariantCulture);
        using var ctx = BDDSetup.CreateContext();

        var user = ctx.Set<User>().FirstOrDefault(u => u.Email == $"{userName}{_emailBase}");
        Assert.That(user, Is.Not.Null, $"User '{userName}' not found");

        var measurementEntity = ctx.Set<Measurement>().FirstOrDefault(m => m.Name == measurement)
            ?? ctx.Set<Measurement>().Add(new Measurement { Name = measurement, Abbreviation = measurement }).Entity;

        var ingredientBase = ctx.Set<IngredientBase>().FirstOrDefault(b => b.Name == ingredientName)
            ?? ctx.Set<IngredientBase>().Add(new IngredientBase { Name = ingredientName }).Entity;

        ctx.SaveChanges();

        var ingredient = new Ingredient
        {
            DisplayName = ingredientName,
            IngredientBase = ingredientBase,
            Measurement = measurementEntity,
            Amount = amount
        };

        var recipe = new Recipe
        {
            Name = $"wvt183-{ingredientName}-{Guid.NewGuid():N}",
            Directions = "test",
            Ingredients = [ingredient]
        };

        var meal = new Meal
        {
            UserId = user!.Id,
            Title = $"wvt183-meal-{Guid.NewGuid():N}",
            StartTime = DateTime.Today,
            Recipes = [recipe]
        };

        ctx.Add(meal);
        ctx.SaveChanges();
    }

    [When("{string} syncs her shopping list for today")]
    public void WhenUserSyncsShoppingListForToday(string userName)
    {
        try { _driver.Manage().Cookies.DeleteCookieNamed("ShoppingListSynced"); } catch { }
        try { _driver.Manage().Cookies.DeleteCookieNamed("ShoppingListDateFrom"); } catch { }
        try { _driver.Manage().Cookies.DeleteCookieNamed("ShoppingListDateTo"); } catch { }

        _driver.Navigate().GoToUrl($"{_baseUrl}/Shopping");
        _wait.Until(d => ((IJavaScriptExecutor)d)
            .ExecuteScript("return document.readyState").ToString() == "complete");
    }

    [Then("the shopping list shows {string} once with amount {string}")]
    public void ThenShoppingListShowsOnceWithAmount(string ingredientName, string expectedAmount)
    {
        _wait.Until(d => ((IJavaScriptExecutor)d)
            .ExecuteScript("return document.readyState").ToString() == "complete");

        var matchingItems = _driver.FindElements(By.CssSelector(".item-display"))
            .Where(i => i.Text.Contains(ingredientName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.That(matchingItems.Count, Is.EqualTo(1),
            $"Expected '{ingredientName}' once on shopping list, found {matchingItems.Count} times");

        var qtyInput = matchingItems[0].FindElement(
            By.XPath("../preceding-sibling::div[1]//input[contains(@class,'qty-input')]"));
        var qtyValue = qtyInput.GetAttribute("value") ?? "";
        Assert.That(qtyValue, Does.Contain(expectedAmount),
            $"Expected amount '{expectedAmount}' in qty value '{qtyValue}' for '{ingredientName}'");
    }

    [When("{string} adds a shopping list item with amount {string} unit {string} and name {string}")]
    public void WhenUserAddsShoppingListItem(string userName, string amount, string unit, string name)
    {
        var amountInput = _wait.Until(d =>
        {
            try { return d.FindElement(By.CssSelector("input[name='amount']")); }
            catch (NoSuchElementException) { return null; }
        });
        Assert.That(amountInput, Is.Not.Null, "Amount input not found on shopping list");
        amountInput!.Clear();
        amountInput.SendKeys(amount);

        new SelectElement(_driver.FindElement(By.CssSelector("select[name='measurement']")))
            .SelectByValue(unit);

        var nameInput = _driver.FindElement(By.CssSelector("input[name='itemName']"));
        nameInput.Clear();
        nameInput.SendKeys(name);

        nameInput.Submit();

        // The JS submit handler calls e.preventDefault() and does an async conflict check
        // before submitting. Wait for either: the conflict modal to appear (conflict found,
        // no navigation), or a success alert (form submitted, page reloaded after redirect).
        _wait.Until(d =>
        {
            try
            {
                var modal = d.FindElement(By.Id("sl-conflict-modal"));
                if (modal.GetCssValue("display") == "flex") return true;
            }
            catch (NoSuchElementException) { }
            catch (StaleElementReferenceException) { }

            try
            {
                return d.FindElements(By.CssSelector(".sl-alert-success"))
                    .Any(e => e.Displayed && !string.IsNullOrEmpty(e.Text));
            }
            catch { }

            return false;
        });
    }

    [Then("{string} appears on the shopping list")]
    public void ThenItemAppearsOnShoppingList(string ingredientName)
    {
        var item = _wait.Until(d =>
        {
            try
            {
                return d.FindElements(By.CssSelector(".item-display"))
                    .FirstOrDefault(i => i.Text.Contains(ingredientName, StringComparison.OrdinalIgnoreCase));
            }
            catch (StaleElementReferenceException) { return null; }
        });
        Assert.That(item, Is.Not.Null, $"Item '{ingredientName}' not found on shopping list");
    }

    [When("{string} submits the shopping list add form with no unit selected for name {string}")]
    public void WhenUserSubmitsAddFormWithNoUnit(string userName, string name)
    {
        // Dismiss the auto-conflict popup if it appeared from prior scenario data
        try
        {
            var backdrop = _driver.FindElement(By.Id("sl-acp-modal"));
            if (backdrop.GetCssValue("display") == "flex")
            {
                _driver.FindElement(By.Id("sl-acp-accept-all")).Click();
                _wait.Until(d =>
                {
                    try { return d.FindElement(By.Id("sl-acp-modal")).GetCssValue("display") != "flex"; }
                    catch (NoSuchElementException) { return true; }
                    catch (StaleElementReferenceException) { return true; }
                });
            }
        }
        catch (NoSuchElementException) { }

        var amountInput = _wait.Until(d =>
        {
            try { return d.FindElement(By.CssSelector("input[name='amount']")); }
            catch (NoSuchElementException) { return null; }
        });
        Assert.That(amountInput, Is.Not.Null, "Amount input not found on shopping list");
        amountInput!.Clear();
        amountInput.SendKeys("1");

        var nameInput = _driver.FindElement(By.CssSelector("input[name='itemName']"));
        nameInput.Clear();
        nameInput.SendKeys(name);

        // Submit without selecting a unit — JS validation intercepts and shows live alert
        _driver.FindElement(By.CssSelector(".sl-add-btn")).Click();
    }

    [Then("a shopping list error message is shown")]
    public void ThenAShoppingListErrorMessageIsShown()
    {
        var errorShown = _wait.Until(d =>
        {
            try
            {
                var liveAlert = d.FindElement(By.Id("sl-live-alert"));
                if (liveAlert.Displayed && !string.IsNullOrEmpty(liveAlert.Text)) return true;

                return d.FindElements(By.CssSelector(".sl-alert-danger"))
                    .Any(e => e.Displayed && !string.IsNullOrEmpty(e.Text));
            }
            catch (NoSuchElementException) { return false; }
            catch (StaleElementReferenceException) { return false; }
        });
        Assert.That(errorShown, Is.True, "Expected a shopping list error message to be shown");
    }

    [Then("the shopping list shows {string} once with amount {string} in {string}")]
    public void ThenShoppingListShowsOnceWithAmountInUnit(string ingredientName, string expectedAmount, string expectedUnit)
    {
        _wait.Until(d => ((IJavaScriptExecutor)d)
            .ExecuteScript("return document.readyState").ToString() == "complete");

        var matchingItems = _driver.FindElements(By.CssSelector(".item-display"))
            .Where(i => i.Text.Contains(ingredientName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.That(matchingItems.Count, Is.EqualTo(1),
            $"Expected '{ingredientName}' once on shopping list, found {matchingItems.Count} times");

        var qtyInput = matchingItems[0].FindElement(
            By.XPath("../preceding-sibling::div[1]//input[contains(@class,'qty-input')]"));
        var qtyValue = qtyInput.GetAttribute("value") ?? "";

        Assert.That(qtyValue, Does.Contain(expectedAmount),
            $"Expected amount '{expectedAmount}' in qty value '{qtyValue}'");
        Assert.That(qtyValue, Does.Contain(expectedUnit).IgnoreCase,
            $"Expected unit '{expectedUnit}' in qty value '{qtyValue}'");
    }

    [Then("a conflict suggestion is shown for {string}")]
    public void ThenAConflictSuggestionIsShownFor(string ingredientName)
    {
        var conflictModal = _wait.Until(d =>
        {
            try
            {
                var modal = d.FindElement(By.Id("sl-conflict-modal"));
                return modal.GetCssValue("display") == "flex" ? modal : null;
            }
            catch (NoSuchElementException) { return null; }
            catch (StaleElementReferenceException) { return null; }
        });
        Assert.That(conflictModal, Is.Not.Null, $"Conflict modal not visible for '{ingredientName}'");

        var modalBody = conflictModal!.FindElement(By.Id("sl-conflict-modal-body"));
        Assert.That(modalBody.Text, Does.Contain(ingredientName).IgnoreCase,
            $"Conflict modal body doesn't mention '{ingredientName}'");
    }

    [Then("{string} appears on the shopping list with amount {string}")]
    public void ThenItemAppearsOnShoppingListWithAmount(string ingredientName, string expectedAmount)
    {
        var matchingItem = _wait.Until(d =>
        {
            try
            {
                return d.FindElements(By.CssSelector(".item-display"))
                    .FirstOrDefault(i => i.Text.Contains(ingredientName, StringComparison.OrdinalIgnoreCase));
            }
            catch (StaleElementReferenceException) { return null; }
        });
        Assert.That(matchingItem, Is.Not.Null, $"Item '{ingredientName}' not found on shopping list");

        var qtyInput = matchingItem!.FindElement(
            By.XPath("../preceding-sibling::div[1]//input[contains(@class,'qty-input')]"));
        var qtyValue = qtyInput.GetAttribute("value") ?? "";
        Assert.That(qtyValue, Does.Contain(expectedAmount),
            $"Expected amount '{expectedAmount}' in qty value '{qtyValue}' for '{ingredientName}'");
    }

    [Then("the auto-conflict popup is shown")]
    public void ThenAutoConflictPopupIsShown()
    {
        var modal = _wait.Until(d =>
        {
            try
            {
                var m = d.FindElement(By.Id("sl-acp-modal"));
                return m.GetCssValue("display") == "flex" ? m : null;
            }
            catch (NoSuchElementException) { return null; }
            catch (StaleElementReferenceException) { return null; }
        });
        Assert.That(modal, Is.Not.Null, "Auto-conflict popup (sl-acp-modal) not visible");
    }

    [When("{string} clicks 'Allow all' on the auto-conflict popup")]
    public void WhenUserClicksAllowAllOnAutoConflictPopup(string userName)
    {
        var btn = _wait.Until(d =>
        {
            try { return d.FindElement(By.Id("sl-acp-accept-all")); }
            catch (NoSuchElementException) { return null; }
            catch (StaleElementReferenceException) { return null; }
        });
        btn!.Click();
        _wait.Until(d =>
        {
            try
            {
                d.FindElement(By.CssSelector(".sl-card"));
                return ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").ToString() == "complete";
            }
            catch (NoSuchElementException) { return false; }
            catch (StaleElementReferenceException) { return false; }
        });
    }

    [When("{string} clicks 'Decline all' on the auto-conflict popup")]
    public void WhenUserClicksDeclineAllOnAutoConflictPopup(string userName)
    {
        var btn = _wait.Until(d =>
        {
            try { return d.FindElement(By.Id("sl-acp-reject-all")); }
            catch (NoSuchElementException) { return null; }
            catch (StaleElementReferenceException) { return null; }
        });
        btn!.Click();
        _wait.Until(d =>
        {
            try
            {
                d.FindElement(By.CssSelector(".sl-card"));
                return ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").ToString() == "complete";
            }
            catch (NoSuchElementException) { return false; }
            catch (StaleElementReferenceException) { return false; }
        });
    }

    [Then("{string} appears {int} times on the shopping list")]
    public void ThenIngredientAppearsNTimesOnShoppingList(string ingredientName, int expectedCount)
    {
        _wait.Until(d => ((IJavaScriptExecutor)d)
            .ExecuteScript("return document.readyState").ToString() == "complete");

        int count = 0;
        _wait.Until(d =>
        {
            try
            {
                count = d.FindElements(By.CssSelector(".item-display"))
                    .Count(i => i.Text.Contains(ingredientName, StringComparison.OrdinalIgnoreCase));
                return true;
            }
            catch (StaleElementReferenceException) { return false; }
        });

        Assert.That(count, Is.EqualTo(expectedCount),
            $"Expected '{ingredientName}' to appear {expectedCount} time(s) on shopping list, found {count}");
    }

    [When("{string} navigates away from the shopping list")]
    public void WhenUserNavigatesAwayFromShoppingList(string userName)
    {
        _driver.Navigate().GoToUrl($"{_baseUrl}/");
        _wait.Until(d => ((IJavaScriptExecutor)d)
            .ExecuteScript("return document.readyState").ToString() == "complete");
    }

    [When("{string} removes her meal containing {string}")]
    public void WhenUserRemovesMealContaining(string userName, string ingredientName)
    {
        using var ctx = BDDSetup.CreateContext();
        var user = ctx.Set<User>().FirstOrDefault(u => u.Email == $"{userName}{_emailBase}");
        Assert.That(user, Is.Not.Null, $"User '{userName}' not found");

        var meals = ctx.Meals
            .Where(m => m.UserId == user!.Id
                     && m.Recipes.Any(r => r.Ingredients.Any(i =>
                            i.IngredientBase.Name == ingredientName)))
            .ToList();

        ctx.Meals.RemoveRange(meals);
        ctx.SaveChanges();
    }

    [Given("{string} has dismissed {string} from her shopping list")]
    public void GivenUserHasDismissedIngredient(string userName, string ingredientName)
    {
        using var ctx = BDDSetup.CreateContext();
        var user = ctx.Set<User>().FirstOrDefault(u => u.Email == $"{userName}{_emailBase}");
        Assert.That(user, Is.Not.Null, $"User '{userName}' not found");

        var ingredientBase = ctx.Set<IngredientBase>().FirstOrDefault(b => b.Name == ingredientName)
            ?? ctx.Set<IngredientBase>().Add(new IngredientBase { Name = ingredientName }).Entity;
        ctx.SaveChanges();

        var alreadyDismissed = ctx.DismissedShoppingItems
            .Any(d => d.UserId == user!.Id && d.IngredientBaseId == ingredientBase.Id);
        if (!alreadyDismissed)
        {
            ctx.DismissedShoppingItems.Add(new DismissedShoppingItem
            {
                UserId = user!.Id,
                IngredientBaseId = ingredientBase.Id
            });
            ctx.SaveChanges();
        }
    }

    [When("{string} re-adds {string} with amount {string} and measurement {string} via batch import")]
    public void WhenUserReAddsViaBatchImport(string userName, string ingredientName, string amount, string measurement)
    {
        var body = $"[{{\"name\":\"{ingredientName}\",\"amount\":{amount},\"measurement\":\"{measurement}\"}}]";
        _driver.Manage().Timeouts().AsynchronousJavaScript = TimeSpan.FromSeconds(10);
        ((IJavaScriptExecutor)_driver).ExecuteAsyncScript(
            @"var done = arguments[arguments.length - 1];
            fetch('/Shopping/AddItemsBatch', {
                method: 'POST',
                headers: {'Content-Type': 'application/json'},
                body: arguments[0]
            }).then(function() { done(); }).catch(function() { done(); });",
            body);
    }

    [When("{string} creates a new meal with ingredient {string} via the meal controller")]
    public void WhenUserCreatesNewMealWithIngredientViaController(string userName, string ingredientName)
    {
        int recipeId;
        using (var ctx = BDDSetup.CreateContext())
        {
            recipeId = ctx.Recipes
                .Where(r => r.Ingredients.Any(i => i.IngredientBase.Name == ingredientName))
                .Select(r => r.Id)
                .FirstOrDefault();
            Assert.That(recipeId, Is.Not.Zero, $"No recipe with ingredient '{ingredientName}' found");
        }

        var today = DateTime.Today;
        var formBody = $"Title=testmeal&SelectedMonth={today.Month}&SelectedDay={today.Day}&RecipeIds={recipeId}";
        ((IJavaScriptExecutor)_driver).ExecuteScript(
            @"fetch('/Meal/NewMeal', {
                method: 'POST',
                headers: {'Content-Type': 'application/x-www-form-urlencoded'},
                body: arguments[0]
            });",
            formBody);
        System.Threading.Thread.Sleep(800);
    }
}
