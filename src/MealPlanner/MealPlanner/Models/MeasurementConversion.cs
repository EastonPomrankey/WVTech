using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MealPlanner.Models;

[Table("MeasurementConversion")]
public class MeasurementConversion
{
    [Key]
    public int Id { get; set; }

    public int FromMeasurementId { get; set; }

    public int ToMeasurementId { get; set; }

    public float Factor { get; set; }
}
