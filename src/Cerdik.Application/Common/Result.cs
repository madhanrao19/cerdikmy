namespace Cerdik.Application.Common;

/// <summary>Lightweight result type used by application services to signal success/failure
/// without throwing for expected domain errors.</summary>
public readonly record struct Result<T>
{
    public bool Succeeded { get; private init; }
    public T? Value { get; private init; }
    public string? Error { get; private init; }
    public string? Code { get; private init; }

    public static Result<T> Ok(T value) => new() { Succeeded = true, Value = value };

    public static Result<T> Fail(string error, string code = "error") =>
        new() { Succeeded = false, Error = error, Code = code };
}

/// <summary>A page of results with total count for server-side pagination.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, long Total, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}

public sealed record PageRequest(int Page = 1, int PageSize = 20, string? Search = null)
{
    public int Skip => (Math.Max(1, Page) - 1) * Math.Clamp(PageSize, 1, 200);
    public int Take => Math.Clamp(PageSize, 1, 200);
}
