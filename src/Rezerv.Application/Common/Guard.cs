using Rezerv.Domain.Common;

namespace Rezerv.Application.Common;

internal static class Guard
{
    public static void PositiveId(int value, string name)
    {
        if (value <= 0)
            throw new DomainException(ErrorCodes.Validation, $"{name} must be a positive number.", ErrorKind.Validation);
    }
}
