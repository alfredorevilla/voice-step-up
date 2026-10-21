using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace StepUp.Infrastructure.Persistence;

/// <summary>Overwrites deleted content with zeros, so a deleted embedding does not linger in free pages.</summary>
public sealed class SecureDeleteInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA secure_delete = ON;";
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA secure_delete = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
