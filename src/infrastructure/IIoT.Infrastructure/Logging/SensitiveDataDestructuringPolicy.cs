using System.Collections;
using System.Reflection;
using Serilog.Core;
using Serilog.Events;

namespace IIoT.Infrastructure.Logging;

public sealed class SensitiveDataDestructuringPolicy : IDestructuringPolicy
{
    public const string RedactedValue = "[REDACTED]";

    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        out LogEventPropertyValue result)
    {
        if (TryDestructureNamedValues(value, propertyValueFactory, out result))
        {
            return true;
        }

        if (!ShouldDestructureObject(value.GetType()))
        {
            result = null!;
            return false;
        }

        var logEventProperties = value.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .Select(property =>
            {
                var propertyValue = property.GetValue(value);

                return new LogEventProperty(
                    property.Name,
                    CreatePropertyValue(property.Name, propertyValue, propertyValueFactory));
            })
            .ToList();

        result = new StructureValue(logEventProperties, value.GetType().Name);
        return true;
    }

    private static bool TryDestructureNamedValues(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        out LogEventPropertyValue result)
    {
        if (value is IDictionary dictionary)
        {
            result = CreateDictionaryValue(dictionary, propertyValueFactory);
            return true;
        }

        if (value is string)
        {
            result = null!;
            return false;
        }

        if (value is IEnumerable enumerable
            && TryGetStringKeyValuePairAccessor(value.GetType(), out var keyAccessor, out var valueAccessor))
        {
            var elements = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>();

            foreach (var entry in enumerable)
            {
                if (entry is null)
                {
                    continue;
                }

                var key = keyAccessor(entry);
                if (key is null)
                {
                    continue;
                }

                elements.Add(new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                    new ScalarValue(key),
                    CreatePropertyValue(key, valueAccessor(entry), propertyValueFactory)));
            }

            result = new DictionaryValue(elements);
            return true;
        }

        result = null!;
        return false;
    }

    private static DictionaryValue CreateDictionaryValue(
        IDictionary dictionary,
        ILogEventPropertyValueFactory propertyValueFactory)
    {
        var elements = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>();

        foreach (DictionaryEntry entry in dictionary)
        {
            var key = entry.Key?.ToString();
            if (key is null)
            {
                continue;
            }

            elements.Add(new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                new ScalarValue(key),
                CreatePropertyValue(key, entry.Value, propertyValueFactory)));
        }

        return new DictionaryValue(elements);
    }

    private static LogEventPropertyValue CreatePropertyValue(
        string? propertyName,
        object? value,
        ILogEventPropertyValueFactory propertyValueFactory)
    {
        if (propertyName is not null && IsSensitiveProperty(propertyName))
        {
            return new ScalarValue(RedactedValue);
        }

        return propertyValueFactory.CreatePropertyValue(value, destructureObjects: true);
    }

    private static bool ShouldDestructureObject(Type type)
    {
        return type.IsClass
               && type != typeof(string)
               && !typeof(Exception).IsAssignableFrom(type)
               && type.Namespace?.StartsWith("IIoT.", StringComparison.Ordinal) == true;
    }

    private static bool TryGetStringKeyValuePairAccessor(
        Type type,
        out Func<object, string?> keyAccessor,
        out Func<object, object?> valueAccessor)
    {
        var keyValueEnumerable = type.GetInterfaces()
            .Append(type)
            .FirstOrDefault(candidate =>
                candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                && candidate.GetGenericArguments()[0].IsGenericType
                && candidate.GetGenericArguments()[0].GetGenericTypeDefinition() == typeof(KeyValuePair<,>)
                && candidate.GetGenericArguments()[0].GetGenericArguments()[0] == typeof(string));

        if (keyValueEnumerable is null)
        {
            keyAccessor = null!;
            valueAccessor = null!;
            return false;
        }

        var pairType = keyValueEnumerable.GetGenericArguments()[0];
        var keyProperty = pairType.GetProperty(nameof(KeyValuePair<string, object>.Key))
                          ?? throw new InvalidOperationException($"Could not resolve {nameof(KeyValuePair<string, object>.Key)} on {pairType.Name}.");
        var valueProperty = pairType.GetProperty(nameof(KeyValuePair<string, object>.Value))
                            ?? throw new InvalidOperationException($"Could not resolve {nameof(KeyValuePair<string, object>.Value)} on {pairType.Name}.");

        keyAccessor = entry => keyProperty.GetValue(entry) as string;
        valueAccessor = entry => valueProperty.GetValue(entry);
        return true;
    }

    private static bool IsSensitiveProperty(string propertyName)
    {
        var normalizedName = propertyName.Trim().ToLowerInvariant();

        if (normalizedName.Contains("password", StringComparison.Ordinal)
            || normalizedName.Contains("secret", StringComparison.Ordinal)
            || normalizedName.Contains("authorization", StringComparison.Ordinal)
            || normalizedName.Contains("cookie", StringComparison.Ordinal))
        {
            return true;
        }

        if (!normalizedName.Contains("token", StringComparison.Ordinal))
        {
            return false;
        }

        return !normalizedName.Contains("expire", StringComparison.Ordinal)
               && !normalizedName.Contains("expiry", StringComparison.Ordinal)
               && !normalizedName.Contains("expiration", StringComparison.Ordinal);
    }
}
