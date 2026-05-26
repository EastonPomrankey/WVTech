using MealPlanner.DAL.Abstract;
using MealPlanner.Models;
using MealPlanner.Models.DTO;
using MealPlanner.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace MealPlanner.Controllers;

[ApiController]
[Route("api/recipe")]
public class RecipeAPIController : ControllerBase
{
    private readonly IRecipeRepository _recipeRepository;
    private readonly IUserRecipeRepository _userRecipeRepository;
    private readonly IRegistrationService _registrationSerivice;
    private readonly MealPlannerDBContext _context;
    private readonly IExternalRecipeService? _recipeService;
    private readonly ITagRepository? _tagRepository;
    private readonly IUserDietaryRestrictionRepository? _userDietaryRestrictionRepo;

    public RecipeAPIController(
        MealPlannerDBContext context,
        IRecipeRepository recipeRepository,
        IUserRecipeRepository userRecipeRepository,
        IRegistrationService registrationService,
        IExternalRecipeService? recipeService = null,
        ITagRepository? tagRepository = null,
        IUserDietaryRestrictionRepository? userDietaryRestrictionRepo = null)
    {
        _context = context;
        _recipeRepository = recipeRepository;
        _userRecipeRepository = userRecipeRepository;
        _registrationSerivice = registrationService;
        _recipeService = recipeService;
        _tagRepository = tagRepository;
        _userDietaryRestrictionRepo = userDietaryRestrictionRepo;
    }

    [HttpGet("tags")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<string>))]
    public async Task<IActionResult> GetRecipeTags()
    {
        if (_tagRepository == null) return Ok(new List<string>());
        var tags = await _tagRepository.GetTagsByPopularityAsync();
        return Ok(tags.Select(t => t.Name).ToList());
    }

    [HttpGet("search")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<RecipeDTO>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SearchRecipesByName(string name, int count=10, string? tag=null)
    {
        IEnumerable<RecipeDTO> results;

        // Resolve active dietary restriction names for the current user (empty when unauthenticated)
        List<string> activeRestrictionNames = [];
        if (_userDietaryRestrictionRepo != null && User.Identity?.IsAuthenticated == true)
        {
            var user = await _registrationSerivice.FindUserByClaimAsync(User);
            if (user != null)
            {
                var userRestrictions = await _userDietaryRestrictionRepo.GetByUserIdAsync(user.Id);
                activeRestrictionNames = userRestrictions
                    .Select(r => r.DietaryRestriction.Name!)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();
            }
        }

        if (!string.IsNullOrEmpty(tag))
        {
            // Existing tag-filter path — unchanged
            results = _recipeRepository.GetRecipesByNameAndTag(name ?? "", tag).Select(r => new RecipeDTO(r)).ToList();
        }
        else if (activeRestrictionNames.Count > 0)
        {
            // New path: filter by user's active dietary restrictions and annotate matching tags
            var recipes = _recipeRepository.GetRecipesByNameAndRestrictions(name, activeRestrictionNames);
            results = recipes.Select(r => new RecipeDTO(r)
            {
                MatchedRestrictionTags = r.Tags
                    .Select(t => t.Name!)
                    .Where(n => activeRestrictionNames.Contains(n, StringComparer.OrdinalIgnoreCase))
                    .ToList()
            });

            if (results.IsNullOrEmpty())
            {
                return NoContent();
            }
        }
        else
        {
            // Existing name-only path with Edamam fallback — unchanged
            results = _recipeRepository.GetRecipesByName(name).Select(r => new RecipeDTO(r)).ToList();
            if (results.Count() < count && _recipeService != null)
            {
                try
                {
                    var externalResults = await _recipeService.SearchExternalRecipesByName(name);
                    results = results.Concat(externalResults).Take(20).ToList();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
        }

        if (results.IsNullOrEmpty())
        {
            return NotFound();
        }

        foreach (RecipeDTO r in results)
        {
            r.VotePercentage = await _userRecipeRepository.GetRecipeVotePercentage(r.Id);
        }

        results = results.OrderByDescending(r => r.VotePercentage).ToList();

        return Ok(results);
    }

    [HttpPut("vote")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeRecipeVote(int recipeId, UserVoteType voteType)
    {
        User? user = await _registrationSerivice.FindUserByClaimAsync(User);
        if (user == null) return Forbid();

        Recipe? recipe = _recipeRepository.Read(recipeId);
        if (recipe == null) return NotFound();

        await _userRecipeRepository.ChangeRecipeVoteAsync(user, recipe, voteType);
        await _context.SaveChangesAsync();
        return Accepted();
    }

    [HttpGet("rating")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(float))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRecipeRating(int recipeId)
    {
        Recipe? recipe = _recipeRepository.Read(recipeId);
        if (recipe == null) return NotFound();

        return Ok(await _userRecipeRepository.GetRecipeVotePercentage(recipeId));
    }

    [HttpGet("external")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(int))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetExternalRecipePage(string externalUri, string recipeName)
    {
        // Load with ingredients so we can detect stubs (saved by MealRepository with no ingredients)
        Recipe? existing = await _context.Set<Recipe>()
            .AsNoTracking()
            .Include(r => r.Ingredients)
            .FirstOrDefaultAsync(r => r.ExternalUri == externalUri);

        // Return early only if the recipe is already fully populated
        if (existing != null && existing.Ingredients.Count > 0) return Ok(existing.Id);

        try
        {
            Recipe? toSave = null;
            if (_recipeService != null)
            {
                toSave = await _recipeService.GetExternalRecipeByURI(externalUri);
            }

            if (toSave != null)
            {
                if (existing != null)
                    toSave.Id = existing.Id; // upgrade existing stub in-place
                _recipeRepository.CreateOrUpdate(toSave);
                await _context.SaveChangesAsync();
            }
            else if (existing == null)
            {
                // No API available and no record yet — save a minimal stub
                toSave = new Recipe { Name = recipeName, ExternalUri = externalUri, Directions = "" };
                _recipeRepository.CreateOrUpdate(toSave);
                await _context.SaveChangesAsync();
            }
        }
        catch (DbUpdateException) {}
        catch (Exception e) { Console.WriteLine(e.Message); }

        var recipe = _recipeRepository.ReadRecipeByExternalUri(externalUri);
        if (recipe == null) return StatusCode(500);
        return Ok(recipe.Id);
    }
    
}