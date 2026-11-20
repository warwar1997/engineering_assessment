namespace Rezerv.Domain.Common;

/// <summary>
/// Thrown when a business rule is violated. The <see cref="Code"/> is a stable,
/// machine readable identifier that the API returns to clients.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string code, string message, ErrorKind kind = ErrorKind.BusinessRule)
        : base(message)
    {
        Code = code;
        Kind = kind;
    }

    public string Code { get; }
    public ErrorKind Kind { get; }

    public static DomainException NotFound(string entity, object id) =>
        new(ErrorCodes.NotFound, $"{entity} '{id}' was not found.", ErrorKind.NotFound);
}

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    BusinessRule
}
