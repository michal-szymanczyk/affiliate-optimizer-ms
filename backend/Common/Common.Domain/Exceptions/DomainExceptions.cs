using Common.Domain.Validation;

namespace Common.Domain.Exceptions;

// Handlers throw the most specific type; ExceptionHandlerMiddleware translates once (ArchitectureSimple.md §2.5, §8.5).

/// <summary>Structurally or semantically invalid request → HTTP 400 with the error list.</summary>
public sealed class ValidationException : Exception
{
    public IReadOnlyList<ValidationError> Errors { get; }

    public ValidationException(IReadOnlyList<ValidationError> errors)
        : this("One or more validation errors occurred.", errors) { }

    public ValidationException(string message, IReadOnlyList<ValidationError> errors) : base(message)
        => Errors = errors;

    public ValidationException(string fieldName, string message)
        : this([new ValidationError { FieldName = fieldName, Message = message }]) { }
}

/// <summary>The addressed entity does not exist → HTTP 404.</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>The request is valid but the current state forbids it → HTTP 412.</summary>
public sealed class LogicErrorException(string message) : Exception(message);
