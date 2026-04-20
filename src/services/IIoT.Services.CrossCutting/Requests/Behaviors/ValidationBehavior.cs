using FluentValidation;
using IIoT.SharedKernel.Result;
using MediatR;

namespace IIoT.Services.CrossCutting.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IReadOnlyCollection<IValidator<TRequest>> _validators = validators.ToArray();

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (_validators.Count == 0)
        {
            return await next(cancellationToken);
        }

        var context = new ValidationContext<TRequest>(request);
        var failures = (await Task.WhenAll(
                _validators.Select(validator => validator.ValidateAsync(context, cancellationToken))))
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .Select(failure => failure.ErrorMessage)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (failures.Length == 0)
        {
            return await next(cancellationToken);
        }

        if (typeof(TResponse) == typeof(Result) ||
            (typeof(TResponse).IsGenericType &&
             typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>)))
        {
            return (TResponse)(dynamic)Result.Invalid(failures);
        }

        throw new ValidationException(string.Join(Environment.NewLine, failures));
    }
}
