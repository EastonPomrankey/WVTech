using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace MealPlanner.Models
{
    public class MealPlannerDBContext : IdentityDbContext<User>
    {
        public MealPlannerDBContext(DbContextOptions<MealPlannerDBContext> options)
            : base(options)
        {
        }

        public DbSet<UserProfile> UserProfiles { get; set; }
        public DbSet<UserNutritionPreference> UserNutritionPreferences { get; set; }
        public DbSet<Meal> Meals { get; set; }
        public DbSet<Recipe> Recipes { get; set; }
        public DbSet<DietaryRestriction> DietaryRestrictions { get; set; }
        public DbSet<UserDietaryRestriction> UserDietaryRestrictions { get; set; }

        public DbSet<Tag> Tags { get; set; }
        public DbSet<UserFoodPreference> UserFoodPreferences { get; set; }
        public DbSet<ShoppingListItem> ShoppingListItems { get; set; }
        public DbSet<DismissedShoppingItem> DismissedShoppingItems { get; set; }
        public DbSet<MealCompletion> MealCompletions { get; set; }
        public DbSet<MealExclusion> MealExclusions { get; set; }
        public DbSet<KrogerExport> KrogerExports { get; set; }
        public DbSet<KrogerExportItem> KrogerExportItems { get; set; }
        public DbSet<MealAutoRemovedIngredient> MealAutoRemovedIngredients { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<MealCompletion>()
                .HasKey(mc => new { mc.MealId, mc.CompletionDate });

            modelBuilder.Entity<MealCompletion>()
                .HasOne(mc => mc.Meal)
                .WithMany()
                .HasForeignKey(mc => mc.MealId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<MealExclusion>()
                .HasKey(me => new { me.MealId, me.ExclusionDate });

            modelBuilder.Entity<MealExclusion>()
                .HasOne(me => me.Meal)
                .WithMany()
                .HasForeignKey(me => me.MealId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserDietaryRestriction>()
                .HasKey(udr => new { udr.UserId, udr.DietaryRestrictionId });

            modelBuilder.Entity<UserDietaryRestriction>()
                .HasOne(udr => udr.User)
                .WithMany()
                .HasForeignKey(udr => udr.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserDietaryRestriction>()
                .HasOne(udr => udr.DietaryRestriction)
                .WithMany()
                .HasForeignKey(udr => udr.DietaryRestrictionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Ingredient>()
                .Navigation(i => i.IngredientBase)
                .AutoInclude();

            modelBuilder.Entity<Ingredient>()
                .Navigation(i => i.Measurement)
                .AutoInclude();

            modelBuilder.Entity<ShoppingListItem>()
                .Navigation(i => i.IngredientBase)
                .AutoInclude();

            modelBuilder.Entity<ShoppingListItem>()
                .Navigation(i => i.Measurement)
                .AutoInclude();
        }
    }
}