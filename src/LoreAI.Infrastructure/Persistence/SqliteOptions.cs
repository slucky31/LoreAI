using System.ComponentModel.DataAnnotations;

namespace LoreAI.Infrastructure.Persistence;

public sealed class SqliteOptions
{
    [Required(AllowEmptyStrings = false)]
    public required string ConnectionString { get; init; }
}
