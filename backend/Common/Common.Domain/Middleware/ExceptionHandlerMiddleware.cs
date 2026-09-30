using System.Text.Json;
using Common.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Common.Domain.Middleware;

public sealed class ExceptionHandlingOptions
{
    public bool HideExceptionDetailsFromClient { get; set; }
}

/// <summary>
/// Lets an application map its own exception types (for example an upstream-service failure) to a status
/// and body without Common.Domain knowing about them. Consulted after the standard mapping, before 500.
/// </summary>
public interface IExceptionResponseMapper
{
    bool TryMap(Exception exception, out int statusCode, out object body);
}

/// <summary>Error-handling contract of the API (ArchitectureSimple.md §8.5). Every exception is logged once here.</summary>
public sealed class ExceptionHandlerMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlerMiddleware> logger,
    IOptions<ExceptionHandlingOptions> options,
    IEnumerable<IExceptionResponseMapper> mappers)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IReadOnlyList<IExceptionResponseMapper> _mappers = mappers.ToArray();

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (context.Response.HasStarted)
            {
                logger.LogError(exception, "Unhandled exception after the response started for {Method} {Path}",
                    context.Request.Method, context.Request.Path);
                throw;
            }

            var (status, body, clientError) = Map(exception);
            if (clientError)
                logger.LogWarning(exception, "{ExceptionType} handled as {Status} for {Method} {Path}",
                    exception.GetType().Name, status, context.Request.Method, context.Request.Path);
            else
                logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);

            context.Response.Clear();
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(JsonSerializer.Serialize(body, Json)).ConfigureAwait(false);
        }
    }

    private (int Status, object Body, bool ClientError) Map(Exception exception)
    {
        switch (exception)
        {
            case ValidationException validation:
                return (StatusCodes.Status400BadRequest, new
                {
                    message = validation.Message,
                    details = validation.Errors.Select(e => new { fieldName = e.FieldName, errorType = e.ErrorType, message = e.Message }),
                }, true);
            case NotFoundException:
                return (StatusCodes.Status404NotFound, new { message = exception.Message }, true);
            case LogicErrorException:
                return (StatusCodes.Status412PreconditionFailed, new { message = exception.Message }, true);
            case UnauthorizedAccessException:
                return (StatusCodes.Status403Forbidden, new { message = exception.Message }, true);
            case OperationCanceledException:
                return (StatusCodes.Status400BadRequest, new { message = "Operation cancelled by user" }, true);
        }

        foreach (var mapper in _mappers)
        {
            if (mapper.TryMap(exception, out var status, out var body))
                return (status, body, status < 500);
        }

        var message = options.Value.HideExceptionDetailsFromClient ? "Unexpected error" : exception.ToString();
        return (StatusCodes.Status500InternalServerError, new { message }, false);
    }
}
