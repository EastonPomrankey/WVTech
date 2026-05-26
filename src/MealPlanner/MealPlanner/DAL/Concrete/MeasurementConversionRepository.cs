using MealPlanner.DAL.Abstract;
using MealPlanner.Models;

namespace MealPlanner.DAL.Concrete;

public class MeasurementConversionRepository : IMeasurementConversionRepository
{
    private readonly MealPlannerDBContext _context;

    public MeasurementConversionRepository(MealPlannerDBContext context)
    {
        _context = context;
    }

    // Normalizes "cups", "Cup(s)", "Tablespoons" → singular lowercase ("cup", "tablespoon").
    private static string NormalizeAliasKey(string name)
    {
        var s = name.Trim().ToLowerInvariant();
        if (s.EndsWith("(s)")) s = s[..^3].TrimEnd();
        return s.TrimEnd('s').Trim();
    }

    public Dictionary<int, (int ToMeasurementId, float Factor)> GetConversionMap()
    {
        var map = _context.MeasurementConversions
            .ToDictionary(c => c.FromMeasurementId, c => (c.ToMeasurementId, c.Factor));

        var allMeasurements = _context.Set<Measurement>().ToList();

        // Resolve alias measurements (e.g. Edamam's "cups", "Cup(s)", "tablespoons") by
        // matching their singular lowercase name to a canonical measurement that has a conversion.
        var uncovered = allMeasurements.Where(m => !map.ContainsKey(m.Id)).ToList();
        if (uncovered.Count > 0)
        {
            var canonicalByKey = map
                .Join(allMeasurements, c => c.Key, m => m.Id,
                      (c, m) => (Key: NormalizeAliasKey(m.Name), Conv: c.Value))
                .GroupBy(x => x.Key)
                .ToDictionary(g => g.Key, g => g.First().Conv);

            foreach (var m in uncovered)
            {
                var key = NormalizeAliasKey(m.Name);
                if (canonicalByKey.TryGetValue(key, out var alias))
                    map[m.Id] = alias;
            }
        }

        // Bridge metric volume (mL/L) into the US volume family (base: tsp) so all
        // liquid measurements are compatible regardless of unit system.
        // 1 mL ≈ 0.202884 tsp  (1 tsp = 4.92892 mL)
        var mLMeasurement = allMeasurements.FirstOrDefault(m =>
            m.Name.Equals("Milliliter", StringComparison.OrdinalIgnoreCase));
        var tspMeasurement = allMeasurements.FirstOrDefault(m =>
            m.Name.Equals("Teaspoon", StringComparison.OrdinalIgnoreCase));

        if (mLMeasurement != null && tspMeasurement != null && map.ContainsKey(mLMeasurement.Id))
        {
            const float mLPerTsp = 0.202884f;
            var toRemap = map
                .Where(kv => kv.Value.ToMeasurementId == mLMeasurement.Id)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var id in toRemap)
                map[id] = (tspMeasurement.Id, map[id].Factor * mLPerTsp);
        }

        return map;
    }
}
