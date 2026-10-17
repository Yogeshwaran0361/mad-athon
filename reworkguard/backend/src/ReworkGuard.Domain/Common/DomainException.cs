namespace ReworkGuard.Domain.Common;

/// <summary>A business rule was violated. Maps to HTTP 409 Conflict.</summary>
public class DomainException : Exception
{
    public DomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
