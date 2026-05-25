using System.Linq.Expressions;
using MealPlanner.DAL.Abstract;
using MealPlanner.Models;
using MealPlanner.Services;

namespace MealPlanner.DAL.Concrete;

public class IngredientBaseRepository : Repository<IngredientBase>, IIngredientBaseRepository
{
    public IngredientBaseRepository(MealPlannerDBContext context) : base(context) { }

    public override IngredientBase CreateOrUpdate(IngredientBase entity)
    {
        entity.Name = IngredientNameNormalizer.NormalizeKey(entity.Name);
        return base.CreateOrUpdate(entity);
    }

    public override IngredientBase FindOrCreate(Expression<Func<IngredientBase, bool>> predicate, Func<IngredientBase> factory)
    {
        return base.FindOrCreate(predicate, () =>
        {
            var entity = factory();
            entity.Name = IngredientNameNormalizer.NormalizeKey(entity.Name);
            return entity;
        });
    }

    public IngredientBase FindOrCreateByName(string name)
    {
        var normalized = IngredientNameNormalizer.NormalizeKey(name);
        return _dbset.Local.FirstOrDefault(ib => ib.Name == normalized)
            ?? _dbset.FirstOrDefault(ib => ib.Name == normalized)
            ?? _dbset.Add(new IngredientBase { Name = normalized }).Entity;
    }

    public IDictionary<string, IngredientBase> GetOrCreateByNames(IEnumerable<string> names)
    {
        var normalized = names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(IngredientNameNormalizer.NormalizeKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new Dictionary<string, IngredientBase>(StringComparer.OrdinalIgnoreCase);
        if (normalized.Count == 0) return result;

        // Pull staged entries from this DbContext first so callers that already
        // FindOrCreate'd a name in the same request don't double-create.
        foreach (var entity in _dbset.Local)
        {
            if (entity.Name != null && normalized.Contains(entity.Name, StringComparer.OrdinalIgnoreCase))
                result[entity.Name] = entity;
        }

        var missing = normalized.Where(n => !result.ContainsKey(n)).ToList();
        if (missing.Count > 0)
        {
            // Single round-trip — IngredientBase.Name has a unique index so this
            // is an index seek over the IN-list, not a table scan.
            var fromDb = _dbset.Where(b => missing.Contains(b.Name)).ToList();
            foreach (var entity in fromDb)
                result[entity.Name] = entity;
        }

        foreach (var name in normalized.Where(n => !result.ContainsKey(n)))
        {
            result[name] = _dbset.Add(new IngredientBase { Name = name }).Entity;
        }

        return result;
    }
}
