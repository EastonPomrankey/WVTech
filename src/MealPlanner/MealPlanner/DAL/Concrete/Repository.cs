using System.Linq.Expressions;
using MealPlanner.DAL.Abstract;
using Microsoft.EntityFrameworkCore;
using MealPlanner.Models;

namespace MealPlanner.DAL.Concrete;

public class Repository<TEntity> : IRepository<TEntity> where TEntity : class, new()
{
    protected readonly DbSet<TEntity> _dbset;

    public Repository(MealPlannerDBContext context)
    {
        _dbset = context.Set<TEntity>();
    }

    /// <summary>
    /// Reads an existing entity in the repository by provided id. 
    /// Returns null if id is not in the repository
    /// </summary>
    /// <param name="id">Id of the entity to be read</param>
    /// <returns>Entity or Null</returns>
    public TEntity? Read(object id)
    {
        return _dbset.Find(id);
    }

    /// <summary>
    /// Reads all entities in the repository which can be further
    /// sorted using LINQ queries.
    /// </summary>
    /// <returns>The repository as a List</returns>
    public List<TEntity> ReadAll()
    {
        return _dbset.ToList();
    }

    /// <summary>
    /// Creates or updates the given entity in the database. If the entity does not have
    /// an id set, it is added to the table the repository represents as a new entry. 
    /// Otherwise it updates an existing entity in the databases.
    /// 
    /// MealPlannerDbContext.SaveChanges() needs to be called after any change to the database
    /// for the changes to be saved.
    /// </summary>
    /// <param name="entity">Entity to be added or changed</param>
    public virtual TEntity CreateOrUpdate(TEntity entity)
    {
        return _dbset.Update(entity).Entity;
    }

    /// <summary>
    /// Deletes an existing entity from the database. Has no effect if the entity is not
    /// already in the database.
    /// 
    /// MealPlannerDbContext.SaveChanges() needs to be called after any change to the database
    /// for the changes to be saved.
    /// </summary>
    /// <param name="entity">Entity to be deleted</param>
    public void Delete(TEntity entity)
    {
        _dbset.Remove(entity);
    }

    /// <summary>
    /// Returns if a given id is found in the repository.
    /// </summary>
    /// <param name="id">Id to find</param>
    /// <returns>true if found, false if not</returns>
    public bool Exists(object id)
    {
        return _dbset.Find(id) is not null;
    }

    /// <summary>
    /// Returns the first entity matching <paramref name="predicate"/>, or adds and returns
    /// the entity produced by <paramref name="factory"/> if no match is found.
    ///
    /// MealPlannerDbContext.SaveChanges() needs to be called after any change to the database
    /// for the changes to be saved.
    /// </summary>
    /// <param name="predicate">Condition used to search for an existing entity</param>
    /// <param name="factory">Produces the new entity when no match is found</param>
    /// <returns>Existing or newly added entity</returns>
    public virtual TEntity FindOrCreate(Expression<Func<TEntity, bool>> predicate, Func<TEntity> factory)
    {
        // Check the local cache first so callers that FindOrCreate the same key
        // multiple times within a single DbContext (e.g. several Edamam recipes
        // each referencing "cup") reuse the staged Added entity instead of
        // adding a second one that would collide on a unique index at save.
        var compiled = predicate.Compile();
        return _dbset.Local.FirstOrDefault(compiled)
            ?? _dbset.FirstOrDefault(predicate)
            ?? _dbset.Add(factory()).Entity;
    }
}