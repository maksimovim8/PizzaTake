using PizzaTake.Core.Services;
using SQLite;

namespace PizzaTake.Core.Repositories;

/// <summary>
/// Base helper for SQLite-backed repositories ensuring database initialization before queries.
/// </summary>
public abstract class SqliteRepositoryBase
{
    private readonly SqliteDatabase database;

    protected SqliteRepositoryBase(SqliteDatabase database)
    {
        this.database = database;
    }

    protected async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        await database.InitializeAsync();
        return database.Connection;
    }

    protected static Task<int> InsertOrUpdateAsync<T>(SQLiteAsyncConnection connection, T entity, int id)
        where T : new() =>
        id != 0 ? connection.UpdateAsync(entity) : connection.InsertAsync(entity);
}
