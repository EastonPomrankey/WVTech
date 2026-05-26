using MealPlanner.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MealPlanner.Migrations
{
    [DbContext(typeof(MealPlannerDBContext))]
    [Migration("20260523000002_WVT183_AddRecipeContributionToShoppingList")]
    public partial class WVT183_AddRecipeContributionToShoppingList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "RecipeContributionAmountInBase",
                table: "ShoppingListItems",
                type: "real",
                nullable: false,
                defaultValue: 0f);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecipeContributionAmountInBase",
                table: "ShoppingListItems");
        }
    }
}
