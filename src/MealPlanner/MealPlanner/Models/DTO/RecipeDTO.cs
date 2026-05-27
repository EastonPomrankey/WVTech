namespace MealPlanner.Models.DTO;

public class RecipeDTO
{
    public RecipeDTO()
    {}

    public RecipeDTO(Recipe recipe)
    {
        Name = recipe.Name;
        Id = recipe.Id;
        ImageUrl = recipe.ImageUrl;
    }
    
    public string? Name { get; set; }
    public int Id { get; set; }
    public float VotePercentage { get; set; }
    public string? ExternalUri { get; set; }
    public string? SourceUrl { get; set; }
    public string? ImageUrl { get; set; }
    public List<string> MatchedRestrictionTags { get; set; } = [];
}