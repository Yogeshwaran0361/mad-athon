namespace ReworkGuard.Application.Common;

public sealed class AuthenticationFailedException : Exception
{
    public AuthenticationFailedException()
        : base("Invalid email or password.")
    {
    }
}
