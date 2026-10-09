namespace Invora.Domain.Common;

public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAtUtc { get; protected set; } = DateTimeOffset.UtcNow;
    public int Version { get; protected set; }
}
public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
