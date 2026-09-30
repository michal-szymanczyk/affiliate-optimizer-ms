using Common.Domain.Requests;
using ValidationException = Common.Domain.Exceptions.ValidationException;

namespace Common.Domain.Validation;

/// <summary>Runs every validator of the request before the handler (ArchitectureSimple.md §4.5). Sequential on purpose.</summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidatorBase<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IReadOnlyList<IValidatorBase<TRequest>> _validators = validators.ToArray();

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (_validators.Count > 0)
        {
            var errors = new List<ValidationError>();
            foreach (var validator in _validators)                      // sequential: validators may use the scoped DbContext
                errors.AddRange(await validator.Validate(request, ct).ConfigureAwait(false));

            if (errors.Count > 0)
                throw new ValidationException(errors);                  // → HTTP 400 by the middleware
        }

        return await next().ConfigureAwait(false);
    }
}
