using MealPlanner.Models;

namespace MealPlanner.DAL.Abstract;

public interface IIngredientBaseRepository : IRepository<IngredientBase>
{
    IngredientBase FindOrCreateByName(string name);

    // Batched FindOrCreate. Returns a dictionary keyed by the normalized name —
    // callers normalize their lookup key with IngredientNameNormalizer.NormalizeKey
    // to retrieve the matching entity.
    IDictionary<string, IngredientBase> GetOrCreateByNames(IEnumerable<string> names);
}
