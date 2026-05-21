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
        _wait.Until(d => ((IJavaScriptExecutor)d)
            .ExecuteScript("return document.readyState").ToString() == "complete");
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
}
