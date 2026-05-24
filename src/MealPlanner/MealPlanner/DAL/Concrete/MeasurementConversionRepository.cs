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

    public Dictionary<int, (int ToMeasurementId, float Factor)> GetConversionMap()
    {
        return _context.MeasurementConversions
            .ToDictionary(c => c.FromMeasurementId, c => (c.ToMeasurementId, c.Factor));
    }
}
