using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;

namespace NeoOrder.OneGate.Data;

/// <summary>
/// Adds missing SQLite tables, ordinary columns and indexes from the EF model.
/// Existing definitions and application data are never rewritten.
/// </summary>
static class DatabaseSchemaExtensions
{
    static readonly SemaphoreSlim InitializationLock = new(1, 1);

    public static void EnsureSchema(this DbContext dbContext)
    {
        dbContext.EnsureSchemaAsync().GetAwaiter().GetResult();
    }

    public static async Task EnsureSchemaAsync(this DbContext dbContext, CancellationToken cancellationToken = default)
    {
        if (!dbContext.Database.IsSqlite())
            throw new NotSupportedException("Automatic schema initialization requires SQLite.");
        if (dbContext.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Initialize the database schema before starting a transaction.");

        await InitializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var creator = dbContext.GetService<IRelationalDatabaseCreator>();
            if (!await creator.ExistsAsync(cancellationToken).ConfigureAwait(false))
                await creator.CreateAsync(cancellationToken).ConfigureAwait(false);

            // Hold the write transaction while inspecting the schema as well as updating it.
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var connection = dbContext.Database.GetDbConnection();
            var model = dbContext.GetService<IDesignTimeModel>().Model;
            var definitions = dbContext.GetService<IMigrationsModelDiffer>()
                .GetDifferences(null, model.GetRelationalModel());
            var objects = await ReadObjectsAsync(connection, transaction.GetDbTransaction(), cancellationToken).ConfigureAwait(false);
            List<MigrationOperation> additions = [];

            foreach (var definition in definitions)
            {
                switch (definition)
                {
                    case CreateTableOperation table:
                        if (!objects.TryGetValue(table.Name, out var existingTable))
                        {
                            additions.Add(table);
                            break;
                        }
                        if (existingTable.Type != "table")
                            throw new InvalidOperationException($"Cannot create table '{table.Name}': another schema object uses that name.");

                        var columns = await ReadColumnsAsync(connection, transaction.GetDbTransaction(),
                            table.Name, cancellationToken).ConfigureAwait(false);
                        ValidatePrimaryKey(table, columns);
                        foreach (var column in table.Columns)
                        {
                            if (columns.TryGetValue(column.Name, out var existingColumn))
                            {
                                ValidateExistingColumn(table, column, existingColumn);
                                continue;
                            }
                            ValidateNewColumn(table, column);
                            additions.Add(column);
                        }
                        break;

                    case CreateIndexOperation index:
                        if (!objects.TryGetValue(index.Name, out var existingIndex))
                            additions.Add(index);
                        else if (existingIndex.Type != "index"
                            || !string.Equals(existingIndex.Table, index.Table, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException($"Cannot create index '{index.Name}': another schema object uses that name.");
                        break;

                    default:
                        throw new NotSupportedException($"Automatic schema initialization does not support '{definition.GetType().Name}'.");
                }
            }

            // Use provider SQL generation so store types, conversions and identifier quoting match EF.
            var commands = dbContext.GetService<IMigrationsSqlGenerator>().Generate(additions, model);
            if (commands.Any(p => p.TransactionSuppressed))
                throw new NotSupportedException("Schema initialization cannot execute operations outside its transaction.");
            var relationalConnection = dbContext.GetService<IRelationalConnection>();
            foreach (var command in commands)
                await command.ExecuteNonQueryAsync(relationalConnection, cancellationToken: cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            InitializationLock.Release();
        }
    }

    static void ValidatePrimaryKey(CreateTableOperation table, Dictionary<string, ColumnSchema> columns)
    {
        string[] actual = columns.Values.Where(p => p.KeyOrdinal > 0)
            .OrderBy(p => p.KeyOrdinal).Select(p => p.Name).ToArray();
        string[] expected = table.PrimaryKey?.Columns ?? [];
        if (!actual.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Table '{table.Name}' has a different primary key; an explicit schema upgrade is required.");
    }

    static void ValidateExistingColumn(CreateTableOperation table, AddColumnOperation column, ColumnSchema existing)
    {
        // SQLite can report notnull=0 for an INTEGER PRIMARY KEY even though it cannot store NULL.
        bool notNull = existing.NotNull || (existing.KeyOrdinal > 0
            && string.Equals(existing.Type, "INTEGER", StringComparison.OrdinalIgnoreCase)
            && table.PrimaryKey?.Columns.Length == 1);
        if (!string.Equals(existing.Type, column.ColumnType, StringComparison.OrdinalIgnoreCase)
            || notNull == column.IsNullable
            || (existing.Hidden != 0) != (column.ComputedColumnSql is not null))
            throw new InvalidOperationException($"Column '{table.Name}.{column.Name}' has an incompatible definition; an explicit schema upgrade is required.");
    }

    static void ValidateNewColumn(CreateTableOperation table, AddColumnOperation column)
    {
        if (table.PrimaryKey?.Columns.Contains(column.Name) == true
            || table.UniqueConstraints.Any(p => p.Columns.Contains(column.Name))
            || table.ForeignKeys.Any(p => p.Columns.Contains(column.Name))
            || table.CheckConstraints.Count != 0
            || column.ComputedColumnSql is not null)
            throw new NotSupportedException($"Column '{table.Name}.{column.Name}' requires constraints or a computed value; an explicit schema upgrade is required.");

        // ADD COLUMN cannot use CURRENT_TIMESTAMP or other SQL expressions as its default.
        if (column.DefaultValueSql is not null || column.DefaultValue == DBNull.Value)
            throw new NotSupportedException($"Column '{table.Name}.{column.Name}' requires a constant default configured with HasDefaultValue.");
        if (!column.IsNullable && column.DefaultValue is null)
            throw new InvalidOperationException($"Column '{table.Name}.{column.Name}' is required. Configure HasDefaultValue or make it nullable before adding it to an existing table.");
    }

    static async Task<Dictionary<string, SchemaObject>> ReadObjectsAsync(
        DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT name, type, tbl_name FROM sqlite_schema WHERE type IN ('table', 'view', 'index')";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, SchemaObject> result = new(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(reader.GetString(0), new(reader.GetString(1), reader.GetString(2)));
        return result;
    }

    static async Task<Dictionary<string, ColumnSchema>> ReadColumnsAsync(
        DbConnection connection, DbTransaction transaction, string table, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT name, type, [notnull], pk, hidden FROM pragma_table_xinfo($table)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$table";
        parameter.Value = table;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, ColumnSchema> result = new(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string name = reader.GetString(0);
            result.Add(name, new(name, reader.GetString(1), reader.GetBoolean(2), reader.GetInt32(3), reader.GetInt32(4)));
        }
        return result;
    }

    sealed record SchemaObject(string Type, string Table);
    sealed record ColumnSchema(string Name, string Type, bool NotNull, int KeyOrdinal, int Hidden);
}
