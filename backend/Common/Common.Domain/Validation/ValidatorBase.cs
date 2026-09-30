using FluentValidation;
using ValidationException = Common.Domain.Exceptions.ValidationException;

namespace Common.Domain.Validation;

/// <summary>Request validator contract (ArchitectureSimple.md §4.5). Discovered by AddClosedImplementations(typeof(IValidatorBase&lt;&gt;), …).</summary>
public interface IValidatorBase<in T> : IValidator<T>
{
    /// <summary>Returns the list of errors (empty when valid). Never throws.</summary>
    Task<List<ValidationError>> Validate(T request, CancellationToken ct = default);

    /// <summary>Throws ValidationException when invalid (used by tests and by manual validation in handlers).</summary>
    Task PerformValidation(T request, CancellationToken ct = default);
}

public abstract class ValidatorBase<T> : AbstractValidator<T>, IValidatorBase<T>
{
    public async Task<List<ValidationError>> Validate(T request, CancellationToken ct = default)
    {
        var result = await ValidateAsync(request, ct).ConfigureAwait(false);
        return result.IsValid
            ? []
            : result.Errors.Select(e => new ValidationError
            {
                FieldName = CamelCasePath(e.PropertyName),
                Message = e.ErrorMessage,
                ErrorType = string.IsNullOrEmpty(e.ErrorCode) ? null : e.ErrorCode,
            }).ToList();
    }

    public async Task PerformValidation(T request, CancellationToken ct = default)
    {
        var errors = await Validate(request, ct).ConfigureAwait(false);
        if (errors.Count > 0) throw new ValidationException(errors);
    }

    /// <summary>"Order.Lines[0].Quantity" → "order.lines[0].quantity".</summary>
    internal static string? CamelCasePath(string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName)) return propertyName;
        return string.Join('.', propertyName.Split('.').Select(segment =>
            segment.Length == 0 || char.IsLower(segment[0]) ? segment : char.ToLowerInvariant(segment[0]) + segment[1..]));
    }
}
