using MealPlanner.Models;

namespace MealPlanner.DAL.Abstract;

public interface IMeasurementRepository : IRepository<Measurement>
{
    Task<List<Measurement>> GetAllOrderedAsync();
    Task<bool> ExistsWithNameAsync(int excludeId, string name);
    Task<Measurement> FindOrCreateByNameAsync(string name);
    Task<bool> IsInUseAsync(int id);

    // Batched FindOrCreate. Returns a dictionary keyed by the trimmed name —
    // callers must trim their lookup key the same way.
    IDictionary<string, Measurement> GetOrCreateByNames(IEnumerable<string> names);
}
