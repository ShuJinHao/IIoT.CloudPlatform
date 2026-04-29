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

        if (InvalidResultResponse<TResponse>.CanCreate)
        {
            return InvalidResultResponse<TResponse>.Create(Result.Invalid(failures));
        }

        throw new ValidationException(string.Join(Environment.NewLine, failures));
    }
}

file static class InvalidResultResponse<TResponse>
{
    private static readonly Func<Result, TResponse>? Factory = CreateFactory();

    public static bool CanCreate => Factory is not null;

    public static TResponse Create(Result result)
    {
        if (Factory is null)
        {
            throw new NotSupportedException(
                $"ValidationBehavior cannot create an invalid response for {typeof(TResponse).FullName}.");
        }

        return Factory(result);
    }

    private static Func<Result, TResponse>? CreateFactory()
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return result => (TResponse)(object)result;
        }

        if (!typeof(TResponse).IsGenericType
            || typeof(TResponse).GetGenericTypeDefinition() != typeof(Result<>))
        {
            return null;
        }

        var conversion = typeof(TResponse).GetMethod(
            "op_Implicit",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
            binder: null,
            types: [typeof(Result)],
            modifiers: null);

        return conversion is null
            ? null
            : result => (TResponse)conversion.Invoke(null, [result])!;
    }
}
