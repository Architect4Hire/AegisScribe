namespace AegisScribe.ApiService.Auth;

public class AuthOperationResult
{
    public bool Succeeded { get; init; }
    public IDictionary<string, string[]> Errors { get; init; } = new Dictionary<string, string[]>();

    public static AuthOperationResult Success() => new() { Succeeded = true };

    public static AuthOperationResult Failure(IDictionary<string, string[]> errors) =>
        new() { Succeeded = false, Errors = errors };
}

public class AuthOperationResult<T> : AuthOperationResult
{
    public T? Value { get; init; }

    public static AuthOperationResult<T> Success(T value) => new() { Succeeded = true, Value = value };

    public static new AuthOperationResult<T> Failure(IDictionary<string, string[]> errors) =>
        new() { Succeeded = false, Errors = errors };
}
