namespace Common.Domain.Validation;

/// <summary>One validation failure. FieldName is camel-cased segment by segment so a client can map it to a form control.</summary>
public sealed record ValidationError
{
    public string? FieldName { get; init; }
    public required string Message { get; init; }
    public string? ErrorType { get; init; }
}
