namespace MealPlanner.DAL.Abstract;

public interface IMeasurementConversionRepository
{
    Dictionary<int, (int ToMeasurementId, float Factor)> GetConversionMap();
}
