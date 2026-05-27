using System.Globalization;
using System.Text.Json;
using MealPlanner.DAL.Abstract;
using MealPlanner.Models;
using MealPlanner.Models.DTO;
using MealPlanner.Services;
using MealPlanner.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace MealPlanner.Controllers;

[Authorize]
public class ShoppingController : Controller
{
    private readonly IShoppingListService _shoppingListService;
    private readonly IPantryService _pantryService;
    private readonly UserManager<User> _userManager;
    private readonly IUserSettingsRepository _userSettingsRepo;
    private readonly IRegistrationService _registrationService;
    private readonly MealPlannerDBContext _context;

    public ShoppingController(
        IShoppingListService shoppingListService,
        IPantryService pantryService,
        UserManager<User> userManager,
        IUserSettingsRepository userSettingsRepo,
        IRegistrationService registrationService,
        MealPlannerDBContext context)
    {
        _shoppingListService = shoppingListService;
        _pantryService = pantryService;
        _userManager = userManager;
        _userSettingsRepo = userSettingsRepo;
        _registrationService = registrationService;
        _context = context;
    }

    private const string AcceptedConflictsCookieName = "ShoppingAccepted";

    private HashSet<(int BaseId, int MeasurementId)> ReadAcceptedConflicts()
    {
        if (!Request.Cookies.TryGetValue(AcceptedConflictsCookieName, out var raw) || string.IsNullOrEmpty(raw))
            return [];
        try
        {
            var pairs = System.Text.Json.JsonSerializer.Deserialize<List<int[]>>(raw);
            return pairs?.Where(p => p.Length == 2).Select(p => (p[0], p[1])).ToHashSet() ?? [];
        }
        catch { return []; }
    }

    private void AppendAcceptedConflicts(IEnumerable<(int BaseId, int MeasurementId)> newPairs)
    {
        var existing = ReadAcceptedConflicts();
        foreach (var pair in newPairs)
            existing.Add(pair);

        var json = System.Text.Json.JsonSerializer.Serialize(
            existing.Select(p => new[] { p.BaseId, p.MeasurementId }).ToList());
        Response.Cookies.Append(AcceptedConflictsCookieName, json, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddDays(30),
            HttpOnly = true,
            SameSite = SameSiteMode.Strict
        });
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index()
    {
        User? user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        DateTime dateFrom = DateTime.Today;
        DateTime dateTo = DateTime.Today;

        if (Request.Cookies.TryGetValue("ShoppingListDateFrom", out var fromStr) &&
            Request.Cookies.TryGetValue("ShoppingListDateTo", out var toStr) &&
            DateTime.TryParse(fromStr, out var cookieFrom) &&
            DateTime.TryParse(toStr, out var cookieTo) &&
            cookieTo >= DateTime.Today)
        {
            dateFrom = cookieFrom;
            dateTo = cookieTo;
        }

        // Clear session-scoped conflict declines on fresh page loads so re-added
        // recipes are re-evaluated. Skip this on redirects from conflict resolution
        // so the decline we just saved isn't immediately erased.
        if (TempData["SkipDeclineClear"] == null)
            _shoppingListService.ClearMeasurementDeclines(user.Id);

        if (TempData["SkipSync"] == null)
            await _shoppingListService.SyncFromDateRangeAsync(user.Id, user, dateFrom, dateTo);

        var items = _shoppingListService.GetItemsForUser(user.Id).ToList();
        var profile = await _userSettingsRepo.GetByUserIdAsync(user.Id);
        var measurements = await _shoppingListService.GetMeasurementsAsync();

        var accepted = ReadAcceptedConflicts();
        var autoAddedConflicts = _shoppingListService.FindAutoAddedConflicts(user.Id, items)
            .Where(c => !accepted.Contains((c.AutoAdded.IngredientBase.Id, c.AutoAdded.MeasurementId)))
            .Select(c => new AutoAddedConflict(
                c.AutoAdded.Id,
                c.AutoAdded.Amount,
                c.AutoAdded.Measurement.Abbreviation ?? c.AutoAdded.Measurement.Name,
                c.AutoAdded.IngredientBase.Name,
                c.Manual.Amount,
                c.Manual.Measurement.Abbreviation ?? c.Manual.Measurement.Name))
            .ToList();

        bool showPantryModal = HttpContext.Session.GetString("ShowPantryModal") == "true";
        if (showPantryModal) HttpContext.Session.Remove("ShowPantryModal");
        ViewBag.ShowPantryModal = showPantryModal;

        return View(new ShoppingListViewModel
        {
            Items = items,
            DateFrom = dateFrom,
            DateTo = dateTo,
            ZipCode = profile?.ZipCode,
            LastStoreId = HttpContext.Session.GetString(KrogerController.SessionStoreId),
            KrogerConnected = !string.IsNullOrEmpty(HttpContext.Session.GetString("KrogerAccessToken")),
            Measurements = measurements,
            AutoAddedConflicts = autoAddedConflicts
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SetDateRange(DateTime dateFrom, DateTime dateTo)
    {
        if (dateFrom > dateTo)
            dateTo = dateFrom;

        var rangeOptions = new CookieOptions { Expires = dateTo.AddDays(1), HttpOnly = true };
        Response.Cookies.Append("ShoppingListDateFrom", dateFrom.ToString("yyyy-MM-dd"), rangeOptions);
        Response.Cookies.Append("ShoppingListDateTo", dateTo.ToString("yyyy-MM-dd"), rangeOptions);

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> GetItemsPartial(string dateFrom, string dateTo)
    {
        User? user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        if (!DateTime.TryParse(dateFrom, out var from)) from = DateTime.Today;
        if (!DateTime.TryParse(dateTo, out var to)) to = DateTime.Today;
        if (from > to) to = from;

        var rangeOptions = new CookieOptions { Expires = to.AddDays(1), HttpOnly = true };
        Response.Cookies.Append("ShoppingListDateFrom", from.ToString("yyyy-MM-dd"), rangeOptions);
        Response.Cookies.Append("ShoppingListDateTo", to.ToString("yyyy-MM-dd"), rangeOptions);

        await _shoppingListService.SyncFromDateRangeAsync(user.Id, user, from, to);

        // A date-range change may introduce new conflicts — reset previously
        // accepted pairs so the conflict popup re-evaluates for the new range.
        Response.Cookies.Delete(AcceptedConflictsCookieName);

        var items = _shoppingListService.GetItemsForUser(user.Id).ToList();

        if (_shoppingListService.FindAutoAddedConflicts(user.Id, items).Any())
        {
            Response.Headers["X-Has-Conflicts"] = "true";
        }

        var measurements = await _shoppingListService.GetMeasurementsAsync();

        Response.Headers["Cache-Control"] = "no-store";

        return PartialView("_ShoppingListItems", new ShoppingListViewModel
        {
            Items = items,
            DateFrom = from,
            DateTo = to,
            Measurements = measurements
        });
    }

    [HttpGet]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> FindConflictsJson(string ingredientName, string measurementName)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var conflicts = _shoppingListService.FindConflictingItems(user.Id, ingredientName, measurementName);
        return Ok(conflicts.Select(i => new
        {
            id = i.Id,
            amount = i.Amount,
            measurementAbbrev = i.Measurement.Abbreviation ?? i.Measurement.Name,
            measurementName = i.Measurement.Name
        }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItem(string itemName, string amount, string measurement, [FromForm] int[]? replaceIds = null)
    {
        User? user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (string.IsNullOrWhiteSpace(itemName))
        {
            TempData["ShoppingListError"] = "Ingredient name is required.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(amount))
        {
            TempData["ShoppingListError"] = "Quantity is required.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(measurement))
        {
            TempData["ShoppingListError"] = "Unit of measurement is required.";
            return RedirectToAction(nameof(Index));
        }

        float? parsedAmount = FractionParser.ParseAmount(amount);
        if (parsedAmount == null || parsedAmount.Value <= 0)
        {
            TempData["ShoppingListError"] = parsedAmount == null
                ? $"Invalid amount \"{amount}\". Use a number, fraction (1/2), or mixed number (1 1/2)."
                : "Quantity must be greater than zero.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            if (replaceIds != null && replaceIds.Length > 0)
            {
                foreach (var id in replaceIds)
                    _shoppingListService.RemoveItem(id, user.Id);
                // Prevent the redirect sync from immediately re-adding the replaced item
                // from recipe contributions before the user sees the clean replacement result.
                TempData["SkipSync"] = true;
            }

            _shoppingListService.AddItem(user.Id, itemName, parsedAmount.Value, measurement, amount.Trim());
            TempData["ShoppingListSuccess"] = $"{itemName} added to your shopping list.";
        }
        catch (ArgumentException ex)
        {
            TempData["ShoppingListError"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateItemAmount(int ingredientBaseId, string newAmount)
    {
        User? user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        float parsedAmount = FractionParser.ParseAmount(newAmount) ?? 0f;
        try
        {
            _shoppingListService.UpdateItemAmount(user.Id, ingredientBaseId, parsedAmount, newAmount?.Trim());
        }
        catch (ArgumentException ex)
        {
            TempData["ShoppingListError"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> UpdateItemAmountJson([FromBody] UpdateAmountRequest request)
    {
        User? user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();
        float? parsedAmount = FractionParser.ParseAmount(request.NewAmount);
        if (parsedAmount == null || parsedAmount.Value <= 0)
            return BadRequest("Invalid amount.");
        _shoppingListService.UpdateItemAmountById(user.Id, request.ItemId, parsedAmount.Value, request.NewAmount?.Trim());
        return Ok();
    }

    public record UpdateAmountRequest(int ItemId, string NewAmount);

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> UpdateItemMeasurementJson([FromBody] UpdateMeasurementRequest request)
    {
        User? user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Measurement))
            return BadRequest("Measurement cannot be empty.");

        var abbreviation = await _shoppingListService.UpdateItemMeasurementAsync(user.Id, request.ItemId, request.Measurement);
        if (abbreviation == null) return NotFound();

        return Ok(new { abbreviation });
    }

    public record UpdateMeasurementRequest(int ItemId, string Measurement);

    public record BatchAddItem(string Name, float Amount, string Measurement);

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> AddItemsBatch([FromBody] List<BatchAddItem> items)
    {
        User? user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        if (items == null || items.Count == 0)
            return BadRequest("No items provided.");

        var batch = items
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .Select(i => (i.Name.Trim(), i.Amount, i.Measurement ?? ""));

        _shoppingListService.AddItemsBatch(user.Id, batch);

        return Ok(new { added = items.Count });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearItems()
    {
        User? user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        _shoppingListService.ClearItems(user.Id);
        TempData["SkipSync"] = true;
        TempData["ShoppingListSuccess"] = "Shopping list cleared.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveItem(int itemId)
    {
        User? user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        _shoppingListService.RemoveItem(itemId, user.Id);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AcceptConflicts([FromForm] int[]? itemIds = null)
    {
        User? user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (itemIds != null && itemIds.Length > 0)
        {
            var pairs = _shoppingListService.GetItemsForUser(user.Id)
                .Where(i => itemIds.Contains(i.Id))
                .Select(i => (i.IngredientBase.Id, i.MeasurementId));
            AppendAcceptedConflicts(pairs);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolveConflicts([FromForm] int[]? itemIds = null)
    {
        User? user = await _userManager.GetUserAsync(User);
        if (user == null) return Challenge();

        if (itemIds != null && itemIds.Length > 0)
            _shoppingListService.ResolveAutoAddedConflicts(user.Id, itemIds);

        TempData["SkipDeclineClear"] = true;
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Pantry()
    {
        var user = await _registrationService.FindUserByClaimAsync(User);
        if (user == null) return Challenge();

        var items = _pantryService.GetPantryItems(user.Id);
        return View(items);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePantryItemAmount(int ingredientId, float? newAmount)
    {
        if (newAmount == null || newAmount <= 0)
        {
            TempData["ValidationError"] = "Amount must be greater than zero.";
            return RedirectToAction(nameof(Pantry));
        }

        var user = await _registrationService.FindUserByClaimAsync(User);
        if (user == null) return Challenge();

        _pantryService.UpdatePantryItemAmount(ingredientId, user.Id, newAmount.Value);
        _context.SaveChanges();
        Response.Cookies.Delete("ShoppingListSynced");

        return RedirectToAction(nameof(Pantry));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemovePantryItem(int ingredientId)
    {
        var user = await _registrationService.FindUserByClaimAsync(User);
        if (user == null) return Challenge();

        _pantryService.RemovePantryItem(ingredientId, user.Id);
        _context.SaveChanges();
        Response.Cookies.Delete("ShoppingListSynced");

        return RedirectToAction(nameof(Pantry));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPantryItem(PantryItemViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ValidationError"] = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .FirstOrDefault() ?? "Please correct the form errors.";
            return RedirectToAction(nameof(Pantry));
        }

        var user = await _registrationService.FindUserByClaimAsync(User);
        if (user == null) return Challenge();

        var ingredient = _pantryService.BuildPantryItem(model.Name, model.Amount, model.Measurement);
        _pantryService.AddPantryItem(user.Id, ingredient);
        _context.SaveChanges();
        Response.Cookies.Delete("ShoppingListSynced");

        TempData["SuccessMessage"] = $"{model.Name} was added to your pantry.";
        return RedirectToAction(nameof(Pantry));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddToPantry()
    {
        var json = HttpContext.Session.GetString("PantryModalItems");
        HttpContext.Session.Remove("PantryModalItems");

        if (string.IsNullOrEmpty(json))
        {
            TempData["ShoppingListError"] = "No items available to add to pantry.";
            return RedirectToAction(nameof(Index));
        }

        var user = await _registrationService.FindUserByClaimAsync(User);
        if (user == null) return Challenge();

        var items = JsonSerializer.Deserialize<List<PantryModalItem>>(json);
        if (items != null)
        {
            foreach (var item in items)
            {
                var ingredient = _pantryService.BuildPantryItem(item.Name, item.Amount, item.Measurement);
                _pantryService.AddPantryItem(user.Id, ingredient);
                _context.SaveChanges();
            }
        }

        TempData["SuccessMessage"] = "Items added to your pantry!";
        return RedirectToAction(nameof(Pantry));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SkipAddToPantry()
    {
        HttpContext.Session.Remove("PantryModalItems");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult TestSetupPantryModal(string names, string amounts, string measurements)
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        if (env != "Development" && env != "Staging")
            return NotFound();

        var nameList = names.Split(',');
        var amountList = amounts.Split(',');
        var measList = measurements.Split(',');

        var items = nameList
            .Zip(amountList, (n, a) => (Name: n.Trim(), Amount: a.Trim()))
            .Zip(measList, (na, m) => new PantryModalItem(
                na.Name,
                float.Parse(na.Amount, CultureInfo.InvariantCulture),
                m.Trim()))
            .ToList();

        HttpContext.Session.SetString("PantryModalItems", JsonSerializer.Serialize(items));
        HttpContext.Session.SetString("ShowPantryModal", "true");
        return Ok();
    }

}
