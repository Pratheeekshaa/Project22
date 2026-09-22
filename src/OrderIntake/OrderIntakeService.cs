using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OrderIntake;

/// <summary>
/// Parses and validates a single laboratory order supplied as a JSON string.
/// Stateless and side-effect free: no console, file, network, or database
/// access, and no unhandled exception ever escapes Process.
/// </summary>
public sealed class OrderIntakeService
{
    private static readonly string[] ValidSpecimenTypes = { "Blood", "Urine", "Tissue", "Saliva" };
    private static readonly string[] ValidPriorities = { "Routine", "Urgent" };

    private const int MaxIdLength = 20;
    private const string DateFormat = "yyyy-MM-dd";

    // Enforces the exact yyyy-MM-dd shape (4-2-2 digits, dash separators)
    // before calendar validity is checked. This is what rejects "2026-9-2",
    // "20-09-2026" and "2026/09/20" as INVALID_FORMAT rather than letting a
    // lenient parser silently accept them.
    private static readonly Regex StrictDatePattern = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

    public OrderResult Process(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Malformed();
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return Malformed();
        }

        using (document)
        {
            var root = document.RootElement;

            // Top-level array, string, number, bool, or null -> not shaped like an order.
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Malformed();
            }

            // Read each recognized field. A field that is present but holds an
            // incompatible JSON type (e.g. "orderId": 123) makes the whole
            // message malformed, per spec. Missing or explicit JSON null is
            // NOT malformed - it is treated as "no value" and surfaces later
            // as a REQUIRED error.
            if (!TryReadStringField(root, "orderId", out var orderIdRaw)) return Malformed();
            if (!TryReadStringField(root, "patientId", out var patientIdRaw)) return Malformed();
            if (!TryReadStringField(root, "specimenId", out var specimenIdRaw)) return Malformed();
            if (!TryReadStringField(root, "specimenType", out var specimenTypeRaw)) return Malformed();
            if (!TryReadStringField(root, "priority", out var priorityRaw)) return Malformed();
            if (!TryReadStringField(root, "collectionDate", out var collectionDateRaw)) return Malformed();
            if (!TryReadStringArrayField(root, "requestedTests", out var requestedTestsRaw)) return Malformed();

            var errors = new List<OrderError>();

            ValidateRequiredId(orderIdRaw, "orderId", errors);
            ValidateRequiredId(patientIdRaw, "patientId", errors);
            ValidateRequiredId(specimenIdRaw, "specimenId", errors);

            var normalizedSpecimenType = ValidateEnumField(specimenTypeRaw, "specimenType", ValidSpecimenTypes, errors);
            var normalizedPriority = ValidateEnumField(priorityRaw, "priority", ValidPriorities, errors);
            var collectionDate = ValidateCollectionDate(collectionDateRaw, errors);
            var validatedTests = ValidateRequestedTests(requestedTestsRaw, errors);

            if (errors.Count > 0)
            {
                return OrderResult.Rejected(errors);
            }

            // errors.Count == 0 guarantees every raw/normalized value below is
            // non-null - each validator only returns null when it also added
            // an error to `errors`.
            var order = new LabOrder(
                orderIdRaw!,
                patientIdRaw!,
                specimenIdRaw!,
                normalizedSpecimenType!,
                normalizedPriority!,
                collectionDate!.Value,
                validatedTests!);

            return OrderResult.Accepted(order);
        }
    }

    private static OrderResult Malformed() =>
        OrderResult.Rejected(new[]
        {
            new OrderError("$", "MALFORMED_INPUT", "The input is not valid JSON or is not shaped like a laboratory order.")
        });

    /// <summary>
    /// Reads a field expected to be a JSON string.
    /// Returns false only when the field is present with an incompatible type.
    /// Missing field or explicit JSON null both yield value = null and return true.
    /// </summary>
    private static bool TryReadStringField(JsonElement root, string name, out string? value)
    {
        value = null;

        if (!root.TryGetProperty(name, out var element))
        {
            return true; // missing entirely
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
                return true; // explicit null -> treated as no value
            case JsonValueKind.String:
                value = element.GetString();
                return true;
            default:
                return false; // e.g. number, object, array, bool -> malformed
        }
    }

    /// <summary>
    /// Reads a field expected to be a JSON array of strings.
    /// Returns false when the field is present but is not an array, or
    /// contains any non-string element.
    /// </summary>
    private static bool TryReadStringArrayField(JsonElement root, string name, out List<string>? value)
    {
        value = null;

        if (!root.TryGetProperty(name, out var element))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var items = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            items.Add(item.GetString()!);
        }

        value = items;
        return true;
    }

    private static void ValidateRequiredId(string? value, string field, List<OrderError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new OrderError(field, "REQUIRED", $"{field} is required."));
            return;
        }

        if (value.Length > MaxIdLength)
        {
            errors.Add(new OrderError(field, "MAX_LENGTH", $"{field} must be at most {MaxIdLength} characters."));
        }
    }

    /// <summary>
    /// Validates a case-insensitive enum-like field (specimenType, priority)
    /// and returns its fixed-form value, or null if it failed validation.
    /// </summary>
    private static string? ValidateEnumField(string? value, string field, string[] allowedValues, List<OrderError> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new OrderError(field, "REQUIRED", $"{field} is required."));
            return null;
        }

        var match = Array.Find(allowedValues, allowed =>
            string.Equals(allowed, value, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            errors.Add(new OrderError(field, "INVALID_VALUE",
                $"{field} must be one of: {string.Join(", ", allowedValues)}."));
            return null;
        }

        return match;
    }

    private static DateOnly? ValidateCollectionDate(string? value, List<OrderError> errors)
    {
        const string field = "collectionDate";

        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new OrderError(field, "REQUIRED", $"{field} is required."));
            return null;
        }

        if (!StrictDatePattern.IsMatch(value) ||
            !DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            errors.Add(new OrderError(field, "INVALID_FORMAT",
                $"{field} must be a real calendar date in yyyy-MM-dd format."));
            return null;
        }

        if (parsed > DateOnly.FromDateTime(DateTime.Today))
        {
            errors.Add(new OrderError(field, "FUTURE_DATE", $"{field} cannot be after today."));
            return null;
        }

        return parsed;
    }

    /// <summary>
    /// Validates requestedTests: required with at least one item, no empty
    /// items, no duplicates ignoring case. Checks are ordered and short-
    /// circuit on the first that applies - one error is enough per spec
    /// ("One DUPLICATE error is enough"), and this keeps the result focused
    /// on the most fundamental problem first.
    /// </summary>
    private static List<string>? ValidateRequestedTests(List<string>? tests, List<OrderError> errors)
    {
        const string field = "requestedTests";

        if (tests is null || tests.Count == 0)
        {
            errors.Add(new OrderError(field, "REQUIRED", $"{field} must contain at least one test."));
            return null;
        }

        if (tests.Any(t => string.IsNullOrWhiteSpace(t)))
        {
            errors.Add(new OrderError(field, "INVALID_VALUE", $"{field} must not contain empty test names."));
            return null;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var test in tests)
        {
            if (!seen.Add(test))
            {
                errors.Add(new OrderError(field, "DUPLICATE", $"{field} must not contain duplicate test names (case-insensitive)."));
                return null;
            }
        }

        return tests;
    }
}
