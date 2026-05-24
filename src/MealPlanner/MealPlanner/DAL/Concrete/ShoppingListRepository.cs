using MealPlanner.DAL.Abstract;
using MealPlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace MealPlanner.DAL.Concrete;

public class ShoppingListRepository : IShoppingListRepository
{
    private readonly MealPlannerDBContext _context;

    public ShoppingListRepository(MealPlannerDBContext context)
    {
        _context = context;
    }

    public void Add(ShoppingListItem item)
    {
        var existing = _context.ShoppingListItems
            .FirstOrDefault(i => i.UserId == item.UserId
                              && i.IngredientBaseId == item.IngredientBase.Id
                              && i.MeasurementId == item.Measurement.Id
                              && i.IsAutoAdded == item.IsAutoAdded);

        if (existing != null)
        {
            existing.Amount += item.Amount;
        }
        else
        {
            _context.ShoppingListItems.Add(item);
        }

        _context.SaveChanges();
    }

    public void AddBatch(IEnumerable<ShoppingListItem> items)
    {
        foreach (var item in items)
        {
            var existing = _context.ShoppingListItems
                .FirstOrDefault(i => i.UserId == item.UserId
                                  && i.IngredientBaseId == item.IngredientBase.Id
                                  && i.MeasurementId == item.Measurement.Id
                                  && i.IsAutoAdded == item.IsAutoAdded);
            if (existing != null)
                existing.Amount += item.Amount;
            else
                _context.ShoppingListItems.Add(item);
        }
        _context.SaveChanges();
    }

    public void AddAutoAddedBatch(IEnumerable<ShoppingListItem> items)
    {
        _context.ShoppingListItems.AddRange(items);
        _context.SaveChanges();
    }

    public void Remove(int itemId, string userId)
    {
        ShoppingListItem? item = _context.ShoppingListItems
            .FirstOrDefault(i => i.Id == itemId && i.UserId == userId);

        if (item != null)
        {
            DismissIngredientBaseInternal(userId, item.IngredientBaseId);
            _context.ShoppingListItems.Remove(item);
            _context.SaveChanges();
        }
    }

    public void PromoteToManual(string userId, IEnumerable<int> itemIds)
    {
        var ids = itemIds.ToHashSet();
        var items = _context.ShoppingListItems
            .Where(i => i.UserId == userId && ids.Contains(i.Id))
            .ToList();
        foreach (var item in items)
            item.IsAutoAdded = false;
        _context.SaveChanges();
    }

    public void RemoveAllByIngredientBase(string userId, int ingredientBaseId)
    {
        var items = _context.ShoppingListItems
            .Where(i => i.UserId == userId && i.IngredientBaseId == ingredientBaseId)
            .ToList();

        if (items.Count > 0)
        {
            _context.ShoppingListItems.RemoveRange(items);
            _context.SaveChanges();
        }
    }

    public void RemoveAutoAddedByUserId(string userId)
    {
        var items = _context.ShoppingListItems
            .Where(i => i.UserId == userId && i.IsAutoAdded)
            .ToList();

        _context.ShoppingListItems.RemoveRange(items);
        _context.SaveChanges();
    }

    public IEnumerable<ShoppingListItem> GetByUserId(string userId)
    {
        return _context.ShoppingListItems
            .Include(i => i.IngredientBase)
            .Include(i => i.Measurement)
            .Where(i => i.UserId == userId)
            .OrderBy(i => i.IsAutoAdded)
            .ThenByDescending(i => i.Id)
            .ToList();
    }

    public void ClearAllItems(string userId)
    {
        var items = _context.ShoppingListItems
            .Where(i => i.UserId == userId)
            .ToList();

        foreach (var item in items)
            DismissIngredientBaseInternal(userId, item.IngredientBaseId);

        _context.ShoppingListItems.RemoveRange(items);
        _context.SaveChanges();
    }

    public void UpdateAmountByIngredientBase(string userId, int ingredientBaseId, float newAmount, string? displayAmount = null)
    {
        var items = _context.ShoppingListItems
            .Where(i => i.UserId == userId && i.IngredientBaseId == ingredientBaseId)
            .ToList();

        if (items.Count == 0) return;

        items[0].Amount = newAmount;
        items[0].DisplayAmount = displayAmount;
        if (items.Count > 1)
            _context.ShoppingListItems.RemoveRange(items.Skip(1));

        _context.SaveChanges();
    }

    public HashSet<int> GetDismissedIngredientBaseIds(string userId)
    {
        return _context.DismissedShoppingItems
            .Where(d => d.UserId == userId && d.MeasurementId == null)
            .Select(d => d.IngredientBaseId)
            .ToHashSet();
    }

    public HashSet<(int IngredientBaseId, int MeasurementId)> GetDeclinedMeasurementPairs(string userId)
    {
        return _context.DismissedShoppingItems
            .Where(d => d.UserId == userId && d.MeasurementId != null)
            .Select(d => new { d.IngredientBaseId, MeasurementId = d.MeasurementId!.Value })
            .AsEnumerable()
            .Select(d => (d.IngredientBaseId, d.MeasurementId))
            .ToHashSet();
    }

    public void DismissIngredientBase(string userId, int ingredientBaseId)
    {
        DismissIngredientBaseInternal(userId, ingredientBaseId);
        _context.SaveChanges();
    }

    public void DismissBatch(string userId, IEnumerable<int> ingredientBaseIds)
    {
        foreach (var id in ingredientBaseIds)
            DismissIngredientBaseInternal(userId, id);
        _context.SaveChanges();
    }

    public void ClearMeasurementDeclines(string userId)
    {
        var rows = _context.DismissedShoppingItems
            .Where(d => d.UserId == userId && d.MeasurementId != null)
            .ToList();
        if (rows.Count > 0)
        {
            _context.DismissedShoppingItems.RemoveRange(rows);
            _context.SaveChanges();
        }
    }

    public void DismissByMeasurement(string userId, int ingredientBaseId, int measurementId)
    {
        var alreadyDeclined = _context.DismissedShoppingItems
            .Any(d => d.UserId == userId && d.IngredientBaseId == ingredientBaseId && d.MeasurementId == measurementId);
        if (!alreadyDeclined)
        {
            _context.DismissedShoppingItems.Add(new DismissedShoppingItem
            {
                UserId = userId,
                IngredientBaseId = ingredientBaseId,
                MeasurementId = measurementId
            });
            _context.SaveChanges();
        }
    }

    public void UnDismiss(string userId, int ingredientBaseId)
    {
        var rows = _context.DismissedShoppingItems
            .Where(d => d.UserId == userId && d.IngredientBaseId == ingredientBaseId)
            .ToList();
        if (rows.Count > 0)
        {
            _context.DismissedShoppingItems.RemoveRange(rows);
            _context.SaveChanges();
        }
    }

    public void DeleteWithoutDismiss(int itemId, string userId)
    {
        var item = _context.ShoppingListItems
            .FirstOrDefault(i => i.Id == itemId && i.UserId == userId);
        if (item != null)
        {
            _context.ShoppingListItems.Remove(item);
            _context.SaveChanges();
        }
    }

    public bool UpdateMeasurementById(string userId, int itemId, int measurementId)
    {
        var item = _context.ShoppingListItems.Find(itemId);
        if (item == null || item.UserId != userId) return false;
        item.MeasurementId = measurementId;
        _context.SaveChanges();
        return true;
    }

    private void DismissIngredientBaseInternal(string userId, int ingredientBaseId)
    {
        var alreadyDismissed = _context.DismissedShoppingItems
            .Any(d => d.UserId == userId && d.IngredientBaseId == ingredientBaseId);
        if (!alreadyDismissed)
        {
            _context.DismissedShoppingItems.Add(new DismissedShoppingItem
            {
                UserId = userId,
                IngredientBaseId = ingredientBaseId
            });
        }
    }
}
