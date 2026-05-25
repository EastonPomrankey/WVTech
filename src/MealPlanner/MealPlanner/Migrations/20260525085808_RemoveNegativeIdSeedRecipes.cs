using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MealPlanner.Migrations
{
    /// <inheritdoc />
    public partial class RemoveNegativeIdSeedRecipes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Recipe",
                keyColumn: "Id",
                keyValue: -9);

            migrationBuilder.DeleteData(
                table: "Recipe",
                keyColumn: "Id",
                keyValue: -8);

            migrationBuilder.DeleteData(
                table: "Recipe",
                keyColumn: "Id",
                keyValue: -7);

            migrationBuilder.DeleteData(
                table: "Recipe",
                keyColumn: "Id",
                keyValue: -6);

            migrationBuilder.DeleteData(
                table: "Recipe",
                keyColumn: "Id",
                keyValue: -5);

            migrationBuilder.DeleteData(
                table: "Recipe",
                keyColumn: "Id",
                keyValue: -4);

            migrationBuilder.DeleteData(
                table: "Recipe",
                keyColumn: "Id",
                keyValue: -3);

            migrationBuilder.DeleteData(
                table: "Recipe",
                keyColumn: "Id",
                keyValue: -2);

            migrationBuilder.DeleteData(
                table: "Recipe",
                keyColumn: "Id",
                keyValue: -1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Recipe",
                columns: new[] { "Id", "Calories", "Carbs", "Directions", "ExternalUri", "Fat", "ImageUrl", "Name", "Protein" },
                values: new object[,]
                {
                    { -9, 300, 0, "", null, 0, null, "Ceasar Salad", 0 },
                    { -8, 400, 0, "", null, 0, null, "Mushroom Steak Salad", 0 },
                    { -7, 850, 0, "", null, 0, null, "Homemade Mac 'n Cheese", 0 },
                    { -6, 550, 0, "", null, 0, null, "Mac 'n Cheese Casserole", 0 },
                    { -5, 400, 0, "", null, 0, null, "Baked Spaghetti Casserole", 0 },
                    { -4, 350, 0, "", null, 0, null, "Vegan Spaghetti with Mushrooms", 0 },
                    { -3, 1000, 0, "", null, 0, null, "Spaghetti and Meatballs", 0 },
                    { -2, 400, 0, "", null, 0, null, "Spaghetti All'assassina", 0 },
                    { -1, 250, 0, "", null, 0, null, "Oatmeal Cookies", 0 }
                });
        }
    }
}
