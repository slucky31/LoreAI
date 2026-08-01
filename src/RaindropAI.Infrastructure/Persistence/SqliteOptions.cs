using System.ComponentModel.DataAnnotations;

namespace RaindropAI.Infrastructure.Persistence;

public sealed class SqliteOptions
{
    [Required(AllowEmptyStrings = false)]
    public required string ConnectionString { get; init; }
}
