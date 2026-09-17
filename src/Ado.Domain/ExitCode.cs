namespace Ado.Domain;

public enum ExitCode
{
    Success = 0,
    Internal = 1,
    Usage = 2,
    Configuration = 3,
    Authentication = 4,
    Authorization = 5,
    NotFound = 6,
    Safety = 7,
    UncertainWrite = 8,
    Transient = 9,
    Partial = 10,
    Cancelled = 130
}
