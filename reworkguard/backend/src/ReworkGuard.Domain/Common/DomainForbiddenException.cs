namespace ReworkGuard.Domain.Common;

/// <summary>A business rule says this actor may not perform the action. Maps to HTTP 403.</summary>
public class DomainForbiddenException : DomainException
{
    public DomainForbiddenException(string code, string message)
        : base(code, message)
    {
    }
}
