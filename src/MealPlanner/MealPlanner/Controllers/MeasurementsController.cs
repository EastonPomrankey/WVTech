using MealPlanner.Models;
using MealPlanner.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MealPlanner.Controllers;

[Authorize(Roles = "Admin")]
public class MeasurementsController : Controller
{
    private readonly MealPlannerDBContext _context;

    public MeasurementsController(MealPlannerDBContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var measurements = await _context.Set<Measurement>().OrderBy(m => m.SortOrder).ToListAsync();
        return View(measurements);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string abbreviation)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(abbreviation))
        {
            TempData["Error"] = "Name and abbreviation are required.";
            return RedirectToAction(nameof(Index));
        }

        name = name.Trim();
        abbreviation = abbreviation.Trim();

        bool exists = await _context.Set<Measurement>()
            .AnyAsync(m => m.Name.ToLower() == name.ToLower());

        if (exists)
        {
            TempData["Error"] = $"A measurement named \"{name}\" already exists.";
            return RedirectToAction(nameof(Index));
        }

        _context.Set<Measurement>().Add(new Measurement { Name = name, Abbreviation = abbreviation });
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Measurement \"{name}\" added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, string name, string abbreviation)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(abbreviation))
        {
            TempData["Error"] = "Name and abbreviation are required.";
            return RedirectToAction(nameof(Index));
        }

        var measurement = await _context.Set<Measurement>().FindAsync(id);
        if (measurement == null)
            return NotFound();

        name = name.Trim();
        abbreviation = abbreviation.Trim();

        bool duplicate = await _context.Set<Measurement>()
            .AnyAsync(m => m.Id != id && m.Name.ToLower() == name.ToLower());

        if (duplicate)
        {
            TempData["Error"] = $"Another measurement named \"{name}\" already exists.";
            return RedirectToAction(nameof(Index));
        }

        measurement.Name = name;
        measurement.Abbreviation = abbreviation;
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Measurement updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var measurement = await _context.Set<Measurement>().FindAsync(id);
        if (measurement == null)
            return NotFound();

        bool inUse = await _context.Set<Ingredient>().AnyAsync(i => i.Measurement.Id == id);
        if (inUse)
        {
            TempData["Error"] = $"Cannot delete \"{measurement.Name}\" — it is used by one or more ingredients.";
            return RedirectToAction(nameof(Index));
        }

        _context.Set<Measurement>().Remove(measurement);
        await _context.SaveChangesAsync();

        TempData["Success"] = $"Measurement \"{measurement.Name}\" deleted.";
        return RedirectToAction(nameof(Index));
    }
}
