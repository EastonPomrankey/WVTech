using MealPlanner.DAL.Abstract;
using MealPlanner.Models;

namespace MealPlanner.Services;

public class ShoppingListService : IShoppingListService
{
    private readonly IShoppingListRepository _shoppingListRepository;
    private readonly IMealRepository _mealRepository;
    private readonly IIngredientBaseRepository _ingredientBaseRepo;
    private readonly IMeasurementRepository _measurementRepo;
    private readonly IMeasurementConversionRepository _conversionRepo;

    public ShoppingListService(IShoppingListRepository shoppingListRepository, IMealRepository mealRepository, IIngredientBaseRepository ingredientBaseRepo, IMeasurementRepository measurementRepo, IMeasurementConversionRepository conversionRepo)
    {
        _shoppingListRepository = shoppingListRepository;
        _mealRepository = mealRepository;
        _ingredientBaseRepo = ingredientBaseRepo;
        _measurementRepo = measurementRepo;
        _conversionRepo = conversionRepo;
    }

    public void ClearMeasurementDeclines(string userId)
    {
        _shoppingListRepository.ClearMeasurementDeclines(userId);
    }

    public async Task SyncFromDateRangeAsync(string userId, User user, DateTime dateFrom, DateTime dateTo)
    {
        var meals = await _mealRepository.GetUserMealsByDateRangeWithIngredientsAsync(user, dateFrom, dateTo);
        var ingredients = meals
            .SelectMany(m => m.Recipes.DistinctBy(r => r.Id).SelectMany(r => r.Ingredients))
            .ToList();
        SyncFromMeals(userId, ingredients);
    }

    private void SyncFromMeals(string userId, IEnumerable<Ingredient> ingredients)
    {
        _shoppingListRepository.RemoveAutoAddedByUserId(userId);

        var manualItems = _shoppingListRepository.GetByUserId(userId).ToList();
        var dismissed = _shoppingListRepository.GetDismissedIngredientBaseIds(userId);
        var declinedPairs = _shoppingListRepository.GetDeclinedMeasurementPairs(userId);
        var conversionMap = _conversionRepo.GetConversionMap();
        var measurementsById = _measurementRepo.ReadAll().ToDictionary(m => m.Id);

        // Collapse same ingredient+unit combinations first (preserving original units)
        var byNameAndUnit = ingredients
            .GroupBy(i => (IngredientNameNormalizer.NormalizeKey(i.IngredientBase.Name), i.Measurement.Id))
            .Select(g => (
                NormalizedName: g.Key.Item1,
                IngredientBase: g.First().IngredientBase,
                Measurement: g.First().Measurement,
                Amount: g.Sum(i => i.Amount)
            ))
            .ToList();

        // Merge compatible units per ingredient; keep original unit when only one unit is used
        var grouped = new List<ShoppingListItem>();
        foreach (var nameGroup in byNameAndUnit.GroupBy(g => g.NormalizedName))
        {
            var subGroups = nameGroup.ToList();

            if (subGroups.Count == 1)
            {
                var sg = subGroups[0];
                grouped.Add(new ShoppingListItem
                {
                    UserId = userId,
                    IngredientBase = sg.IngredientBase,
                    Measurement = sg.Measurement,
                    Amount = sg.Amount,
                    IsAutoAdded = true
                });
            }
            else
            {
                foreach (var familyGroup in subGroups.GroupBy(sg =>
                    conversionMap.TryGetValue(sg.Measurement.Id, out var conv)
                        ? conv.ToMeasurementId
                        : sg.Measurement.Id))
                {
                    int baseMeasurementId = familyGroup.Key;
                    var baseMeasurement = measurementsById.TryGetValue(baseMeasurementId, out var bm)
                        ? bm : familyGroup.First().Measurement;

                    float totalAmount = familyGroup.Sum(sg =>
                        conversionMap.TryGetValue(sg.Measurement.Id, out var conv)
                            ? sg.Amount * conv.Factor
                            : sg.Amount);

                    grouped.Add(new ShoppingListItem
                    {
                        UserId = userId,
                        IngredientBase = familyGroup.First().IngredientBase,
                        Measurement = baseMeasurement,
                        Amount = totalAmount,
                        IsAutoAdded = true
                    });
                }
            }
        }

        var toAdd = new List<ShoppingListItem>();
        var updatedManualIds = new HashSet<int>();

        foreach (var item in grouped)
        {
            if (dismissed.Contains(item.IngredientBase.Id))
                continue;

            if (declinedPairs.Contains((item.IngredientBase.Id, item.Measurement.Id)))
                continue;

            bool hasConv = conversionMap.TryGetValue(item.Measurement.Id, out var recipeConv);
            float recipeContribInBase = hasConv ? item.Amount * recipeConv.Factor : item.Amount;
            int recipeBaseId = hasConv ? recipeConv.ToMeasurementId : item.Measurement.Id;

            var normalizedName = IngredientNameNormalizer.NormalizeKey(item.IngredientBase.Name);
            var compatibleManual = manualItems.FirstOrDefault(m =>
                string.Equals(IngredientNameNormalizer.NormalizeKey(m.IngredientBase.Name), normalizedName, StringComparison.OrdinalIgnoreCase)
                && (conversionMap.TryGetValue(m.MeasurementId, out var mc)
                    ? mc.ToMeasurementId : m.MeasurementId) == recipeBaseId);

            if (compatibleManual != null)
            {
                float userPure = MathF.Max(0f,
                    ToBase(compatibleManual.Amount, compatibleManual.MeasurementId, conversionMap)
                    - compatibleManual.RecipeContributionAmountInBase);
                float newTotalInBase = userPure + recipeContribInBase;
                // Convert back to the item's own unit so Amount stays consistent with MeasurementId.
                float newAmountInItemUnit = conversionMap.TryGetValue(compatibleManual.MeasurementId, out var itemConv)
                    ? newTotalInBase / itemConv.Factor
                    : newTotalInBase;
                _shoppingListRepository.UpdateAmountAndRecipeContribution(
                    userId, compatibleManual.Id,
                    newAmountInItemUnit,
                    recipeContribInBase);
                updatedManualIds.Add(compatibleManual.Id);
            }
            else
            {
                toAdd.Add(item);
            }
        }

        // Recipe removed: zero out the stale recipe contribution from any manual item not updated
        foreach (var manual in manualItems.Where(m => m.RecipeContributionAmountInBase > 0 && !updatedManualIds.Contains(m.Id)))
        {
            float userPure = MathF.Max(0f,
                ToBase(manual.Amount, manual.MeasurementId, conversionMap)
                - manual.RecipeContributionAmountInBase);
            // Convert back to the item's own unit.
            float restoredInItemUnit = conversionMap.TryGetValue(manual.MeasurementId, out var restoreConv)
                ? userPure / restoreConv.Factor
                : userPure;
            _shoppingListRepository.UpdateAmountAndRecipeContribution(userId, manual.Id, restoredInItemUnit, 0f);
        }

        if (toAdd.Count > 0)
            _shoppingListRepository.AddAutoAddedBatch(toAdd);
    }

    public void AddItem(string userId, string itemName, float amount, string measurement, string? displayAmount = null)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            throw new ArgumentException("Item name cannot be empty.");

        var ingredientBase = _ingredientBaseRepo.FindOrCreateByName(itemName);

        var allMeasurements = _measurementRepo.ReadAll();
        var trimmed = measurement.Trim();
        var measurementEntity = allMeasurements
            .FirstOrDefault(m => m.Name.ToLower() == trimmed.ToLower()
                              || m.Abbreviation.ToLower() == trimmed.ToLower());
        if (measurementEntity == null)
            throw new ArgumentException($"Unknown measurement '{trimmed}'.");

        _shoppingListRepository.UnDismiss(userId, ingredientBase.Id);

        var conversionMap = _conversionRepo.GetConversionMap();
        var measurementsById = allMeasurements.ToDictionary(m => m.Id);

        bool newHasConv = conversionMap.TryGetValue(measurementEntity.Id, out var newConv);
        int newBaseId = newHasConv ? newConv.ToMeasurementId : measurementEntity.Id;
        float newAmountInBase = newHasConv ? amount * newConv.Factor : amount;

        var normalizedName = IngredientNameNormalizer.NormalizeKey(ingredientBase.Name);
        var compatible = _shoppingListRepository.GetByUserId(userId)
            .Where(i => IngredientNameNormalizer.NormalizeKey(i.IngredientBase.Name) == normalizedName
                     && (conversionMap.TryGetValue(i.MeasurementId, out var ic) ? ic.ToMeasurementId : i.MeasurementId) == newBaseId)
            .ToList();

        if (compatible.Count > 0)
        {
            float existingTotalInBase = MathF.Max(0f,
                compatible.Sum(i => ToBase(i.Amount, i.MeasurementId, conversionMap)));
            // Auto-added items are entirely recipe contribution; manual items track their own.
            float existingRecipeContrib = compatible.Sum(i =>
                i.IsAutoAdded
                    ? MathF.Max(0f, ToBase(i.Amount, i.MeasurementId, conversionMap))
                    : i.RecipeContributionAmountInBase);
            float totalInBase = existingTotalInBase + newAmountInBase;

            foreach (var old in compatible)
                _shoppingListRepository.DeleteWithoutDismiss(old.Id, userId);

            // Store in the largest compatible unit (e.g. cups instead of tsp) for readability.
            float bestFactor = newHasConv ? newConv.Factor : 1f;
            int bestMeasurementId = measurementEntity.Id;
            foreach (var mid in compatible.Select(i => i.MeasurementId).Distinct())
            {
                if (conversionMap.TryGetValue(mid, out var mc) && mc.Factor > bestFactor)
                {
                    bestFactor = mc.Factor;
                    bestMeasurementId = mid;
                }
            }
            var bestMeasurement = measurementsById.TryGetValue(bestMeasurementId, out var bm) ? bm : measurementEntity;

            _shoppingListRepository.Add(new ShoppingListItem
            {
                UserId = userId,
                IngredientBase = ingredientBase,
                Measurement = bestMeasurement,
                Amount = totalInBase / bestFactor,
                IsAutoAdded = false,
                RecipeContributionAmountInBase = existingRecipeContrib
            });
        }
        else
        {
            _shoppingListRepository.Add(new ShoppingListItem
            {
                UserId = userId,
                IngredientBase = ingredientBase,
                Measurement = measurementEntity,
                Amount = amount,
                DisplayAmount = displayAmount,
                IsAutoAdded = false
            });
        }
    }

    public void AddItemsBatch(string userId, IEnumerable<(string name, float amount, string measurement)> items)
    {
        var allMeasurements = _measurementRepo.ReadAll();
        var toAdd = new List<ShoppingListItem>();

        foreach (var (name, amount, measurement) in items)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;

            var trimmed = measurement.Trim();
            var measurementEntity = allMeasurements
                .FirstOrDefault(m => string.Equals(m.Name, trimmed, StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(m.Abbreviation, trimmed, StringComparison.OrdinalIgnoreCase));
            if (measurementEntity == null) continue;

            var ingredientBase = _ingredientBaseRepo.FindOrCreateByName(name);

            toAdd.Add(new ShoppingListItem
            {
                UserId = userId,
                IngredientBase = ingredientBase,
                Measurement = measurementEntity,
                Amount = amount,
                IsAutoAdded = false
            });
        }

        _shoppingListRepository.AddBatch(toAdd);
    }

    public void RemoveItem(int itemId, string userId)
    {
        if (itemId <= 0)
            throw new ArgumentException("Invalid item id.");

        _shoppingListRepository.Remove(itemId, userId);
    }

    public void ResolveAutoAddedConflicts(string userId, IEnumerable<int> autoItemIds)
    {
        var idsToRemove = autoItemIds.ToHashSet();
        var allItems = _shoppingListRepository.GetByUserId(userId).ToList();

        foreach (var item in allItems.Where(i => idsToRemove.Contains(i.Id)))
        {
            // Decline only this specific measurement — the ingredient itself is not
            // blocked, so other measurements (e.g. tsp vs count) can still be
            // auto-added from recipes on future syncs.
            _shoppingListRepository.DismissByMeasurement(userId, item.IngredientBase.Id, item.MeasurementId);
            _shoppingListRepository.DeleteWithoutDismiss(item.Id, userId);
        }
    }

    public void UpdateItemAmount(string userId, int ingredientBaseId, float newAmount, string? displayAmount = null)
    {
        if (newAmount <= 0)
            throw new ArgumentException("Amount must be greater than zero.");

        _shoppingListRepository.UpdateAmountByIngredientBase(userId, ingredientBaseId, newAmount, displayAmount);
    }

    public void ClearItems(string userId)
    {
        _shoppingListRepository.ClearAllItems(userId);
    }

    public async Task<string?> UpdateItemMeasurementAsync(string userId, int itemId, string measurementName)
    {
        var measurement = await _measurementRepo.FindOrCreateByNameAsync(measurementName.Trim());
        return _shoppingListRepository.UpdateMeasurementById(userId, itemId, measurement.Id)
            ? (measurement.Abbreviation ?? measurement.Name)
            : null;
    }

    public Task<List<Measurement>> GetMeasurementsAsync()
    {
        return _measurementRepo.GetAllOrderedAsync();
    }

    public IEnumerable<ShoppingListItem> GetItemsForUser(string userId)
    {
        return _shoppingListRepository.GetByUserId(userId);
    }

    public IEnumerable<(ShoppingListItem AutoAdded, ShoppingListItem Manual)> FindAutoAddedConflicts(string userId, IReadOnlyList<ShoppingListItem>? cachedItems = null)
    {
        var conversionMap = _conversionRepo.GetConversionMap();
        var allItems = cachedItems?.ToList() ?? _shoppingListRepository.GetByUserId(userId).ToList();
        var autoItems = allItems.Where(i => i.IsAutoAdded).ToList();

        var results = new List<(ShoppingListItem, ShoppingListItem)>();
        var reportedIds = new HashSet<int>();

        foreach (var auto in autoItems)
        {
            if (reportedIds.Contains(auto.Id)) continue;

            var normalizedName = IngredientNameNormalizer.NormalizeKey(auto.IngredientBase.Name);
            int autoBase = conversionMap.TryGetValue(auto.MeasurementId, out var ac)
                ? ac.ToMeasurementId
                : auto.MeasurementId;

            var conflict = allItems.FirstOrDefault(m =>
            {
                if (m.Id == auto.Id || reportedIds.Contains(m.Id)) return false;
                if (IngredientNameNormalizer.NormalizeKey(m.IngredientBase.Name) != normalizedName) return false;
                int itemBase = conversionMap.TryGetValue(m.MeasurementId, out var mc)
                    ? mc.ToMeasurementId
                    : m.MeasurementId;
                return itemBase != autoBase;
            });

            if (conflict != null)
            {
                results.Add((auto, conflict));
                reportedIds.Add(auto.Id);
                reportedIds.Add(conflict.Id);
            }
        }

        return results;
    }

    public IEnumerable<ShoppingListItem> FindConflictingItems(string userId, string ingredientName, string addedMeasurementName)
    {
        var conversionMap = _conversionRepo.GetConversionMap();
        var allMeasurements = _measurementRepo.ReadAll();
        var trimmed = addedMeasurementName.Trim();

        var addedMeasurement = allMeasurements.FirstOrDefault(m =>
            string.Equals(m.Name, trimmed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(m.Abbreviation, trimmed, StringComparison.OrdinalIgnoreCase));

        if (addedMeasurement == null) return [];

        int? addedBaseUnitId = conversionMap.TryGetValue(addedMeasurement.Id, out var addedConv)
            ? addedConv.ToMeasurementId
            : (int?)null;

        var normalizedName = IngredientNameNormalizer.NormalizeKey(ingredientName);

        return _shoppingListRepository.GetByUserId(userId)
            .Where(i => IngredientNameNormalizer.NormalizeKey(i.IngredientBase.Name) == normalizedName
                     && i.MeasurementId != addedMeasurement.Id)
            .Where(i =>
            {
                int? itemBaseUnitId = conversionMap.TryGetValue(i.MeasurementId, out var itemConv)
                    ? itemConv.ToMeasurementId
                    : (int?)null;
                return itemBaseUnitId != addedBaseUnitId;
            })
            .ToList();
    }

    private static float ToBase(float amount, int measurementId, Dictionary<int, (int ToMeasurementId, float Factor)> conversionMap)
        => conversionMap.TryGetValue(measurementId, out var conv) ? amount * conv.Factor : amount;
}
