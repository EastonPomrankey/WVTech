using MealPlanner.DAL.Abstract;
using MealPlanner.Models;
using Microsoft.EntityFrameworkCore;

namespace MealPlanner.DAL.Concrete;

public class MeasurementRepository : Repository<Measurement>, IMeasurementRepository
{
    private readonly MealPlannerDBContext _context;

    public MeasurementRepository(MealPlannerDBContext context) : base(context)
    {
        _context = context;
    }

    public async Task<List<Measurement>> GetAllOrderedAsync()
    {
        return await _dbset
            .Where(m => m.Abbreviation != "")
            .OrderBy(m => m.SortOrder)
            .ToListAsync();
    }

    public async Task<bool> ExistsWithNameAsync(int excludeId, string name)
    {
        return await _dbset.AnyAsync(m => m.Id != excludeId && m.Name.ToLower() == name.ToLower());
    }

    public async Task<Measurement> FindOrCreateByNameAsync(string name)
    {
        var trimmed = name.Trim();
        var existing = await _dbset.FirstOrDefaultAsync(m =>
            m.Name.ToLower() == trimmed.ToLower() ||
            m.Abbreviation.ToLower() == trimmed.ToLower());

        if (existing != null) return existing;

        var measurement = new Measurement { Name = trimmed, Abbreviation = trimmed };
        _dbset.Add(measurement);
        await _context.SaveChangesAsync();
        return measurement;
    }

    public async Task<bool> IsInUseAsync(int id)
        => await _context.Set<Ingredient>().AnyAsync(i => i.Measurement.Id == id);

    public IDictionary<string, Measurement> GetOrCreateByNames(IEnumerable<string> names)
    {
        var trimmed = names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new Dictionary<string, Measurement>(StringComparer.OrdinalIgnoreCase);
        if (trimmed.Count == 0) return result;

        foreach (var entity in _dbset.Local)
        {
            if (entity.Name != null && trimmed.Contains(entity.Name, StringComparer.OrdinalIgnoreCase))
                result[entity.Name] = entity;
        }

        var missing = trimmed.Where(n => !result.ContainsKey(n)).ToList();
        if (missing.Count > 0)
        {
            // Single round-trip — Measurement.Name has a unique index.
            var fromDb = _dbset.Where(m => missing.Contains(m.Name)).ToList();
            foreach (var entity in fromDb)
                result[entity.Name] = entity;
        }

        foreach (var name in trimmed.Where(n => !result.ContainsKey(n)))
        {
            result[name] = _dbset.Add(new Measurement { Name = name, Abbreviation = name }).Entity;
        }

        return result;
    }
}
