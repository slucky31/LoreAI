using System.ComponentModel.DataAnnotations;

namespace LoreAI.Infrastructure.Persistence;

public sealed class PostgresOptions
{
    [Required(AllowEmptyStrings = false)]
    public required string ConnectionString { get; init; }
}
