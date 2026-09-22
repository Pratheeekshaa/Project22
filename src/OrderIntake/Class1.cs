using System.Globalization;
using System.Text.Json;

namespace OrderIntake;

public enum OrderStatus
{
	Accepted,
	Rejected
}

public sealed class ValidationError
{
	public ValidationError(string field, string code, string message)
	{
		Field = field;
		Code = code;
		Message = message;
	}

	public string Field { get; }
	public string Code { get; }
	public string Message { get; }
}

public sealed class LaboratoryOrder
{
	public string OrderId { get; init; } = string.Empty;
	public string PatientId { get; init; } = string.Empty;
	public string SpecimenId { get; init; } = string.Empty;
	public string SpecimenType { get; init; } = string.Empty;
	public string Priority { get; init; } = string.Empty;
	public DateOnly CollectionDate { get; init; }
	public IReadOnlyList<string> RequestedTests { get; init; } = Array.Empty<string>();
}

public sealed class OrderResult
{
	public OrderStatus Status { get; init; }
	public LaboratoryOrder? Order { get; init; }
	public IReadOnlyList<ValidationError> Errors { get; init; } = Array.Empty<ValidationError>();
}

public sealed class OrderIntakeService
{
	private static readonly HashSet<string> SpecimenTypes = new(StringComparer.OrdinalIgnoreCase)
	{
		"Blood", "Urine", "Tissue", "Saliva"
	};

	private static readonly HashSet<string> Priorities = new(StringComparer.OrdinalIgnoreCase)
	{
		"Routine", "Urgent"
	};

	public OrderResult Process(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return MalformedResult();
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			if (document.RootElement.ValueKind != JsonValueKind.Object || !HasCompatibleShape(document.RootElement))
			{
				return MalformedResult();
			}

			JsonElement root = document.RootElement;
			List<ValidationError> errors = Validate(root);
			if (errors.Count > 0)
			{
				return new OrderResult { Status = OrderStatus.Rejected, Errors = errors };
			}

			return new OrderResult
			{
				Status = OrderStatus.Accepted,
				Order = BuildOrder(root)
			};
		}
		catch (JsonException)
		{
			return MalformedResult();
		}
		catch (ArgumentException)
		{
			return MalformedResult();
		}
	}

	private static bool HasCompatibleShape(JsonElement root)
	{
		foreach (JsonProperty property in root.EnumerateObject())
		{
			bool compatible = property.Name switch
			{
				"orderId" or "patientId" or "specimenId" or "specimenType" or "priority" or "collectionDate" =>
					property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Null,
				"requestedTests" => HasCompatibleTests(property.Value),
				_ => true
			};

			if (!compatible)
			{
				return false;
			}
		}

		return true;
	}

	private static bool HasCompatibleTests(JsonElement value)
	{
		if (value.ValueKind == JsonValueKind.Null)
		{
			return true;
		}

		if (value.ValueKind != JsonValueKind.Array)
		{
			return false;
		}

		return value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String);
	}

	private static List<ValidationError> Validate(JsonElement root)
	{
		List<ValidationError> errors = new();
		ValidateRequiredLength(root, "orderId", errors);
		ValidateRequiredLength(root, "patientId", errors);
		ValidateRequiredLength(root, "specimenId", errors);
		ValidateChoice(root, "specimenType", SpecimenTypes, errors);
		ValidateChoice(root, "priority", Priorities, errors);
		ValidateDate(root, errors);
		ValidateTests(root, errors);
		return errors;
	}

	private static void ValidateRequiredLength(JsonElement root, string field, List<ValidationError> errors)
	{
		string? value = GetString(root, field);
		if (string.IsNullOrWhiteSpace(value))
		{
			errors.Add(new ValidationError(field, "REQUIRED", $"{field} is required."));
		}
		else if (value.Trim().Length > 20)
		{
			errors.Add(new ValidationError(field, "MAX_LENGTH", $"{field} must be at most 20 characters."));
		}
	}

	private static void ValidateChoice(JsonElement root, string field, HashSet<string> allowed, List<ValidationError> errors)
	{
		string? value = GetString(root, field);
		if (string.IsNullOrWhiteSpace(value))
		{
			errors.Add(new ValidationError(field, "REQUIRED", $"{field} is required."));
		}
		else if (!allowed.Contains(value.Trim()))
		{
			errors.Add(new ValidationError(field, "INVALID_VALUE", $"{field} has an invalid value."));
		}
	}

	private static void ValidateDate(JsonElement root, List<ValidationError> errors)
	{
		string? value = GetString(root, "collectionDate");
		if (string.IsNullOrWhiteSpace(value))
		{
			errors.Add(new ValidationError("collectionDate", "REQUIRED", "collectionDate is required."));
			return;
		}

		if (!DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
		{
			errors.Add(new ValidationError("collectionDate", "INVALID_FORMAT", "collectionDate must be a real date in yyyy-MM-dd format."));
		}
		else if (date > DateOnly.FromDateTime(DateTime.Today))
		{
			errors.Add(new ValidationError("collectionDate", "FUTURE_DATE", "collectionDate must not be in the future."));
		}
	}

	private static void ValidateTests(JsonElement root, List<ValidationError> errors)
	{
		if (!root.TryGetProperty("requestedTests", out JsonElement value) || value.ValueKind == JsonValueKind.Null)
		{
			errors.Add(new ValidationError("requestedTests", "REQUIRED", "requestedTests is required."));
			return;
		}

		List<string> tests = value.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToList();
		if (tests.Count == 0 || tests.Any(string.IsNullOrWhiteSpace))
		{
			errors.Add(new ValidationError("requestedTests", "INVALID_VALUE", "requestedTests must contain at least one non-empty item."));
		}

		if (tests.GroupBy(test => test.Trim(), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
		{
			errors.Add(new ValidationError("requestedTests", "DUPLICATE", "requestedTests must not contain duplicates."));
		}
	}

	private static LaboratoryOrder BuildOrder(JsonElement root)
	{
		string specimenType = GetString(root, "specimenType")!.Trim();
		string priority = GetString(root, "priority")!.Trim();
		return new LaboratoryOrder
		{
			OrderId = GetString(root, "orderId")!.Trim(),
			PatientId = GetString(root, "patientId")!.Trim(),
			SpecimenId = GetString(root, "specimenId")!.Trim(),
			SpecimenType = Canonicalize(specimenType, SpecimenTypes),
			Priority = Canonicalize(priority, Priorities),
			CollectionDate = DateOnly.ParseExact(GetString(root, "collectionDate")!.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture),
			RequestedTests = root.GetProperty("requestedTests").EnumerateArray().Select(item => item.GetString()!.Trim()).ToList()
		};
	}

	private static string? GetString(JsonElement root, string field)
	{
		return root.TryGetProperty(field, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
	}

	private static string Canonicalize(string value, HashSet<string> allowed)
	{
		return allowed.First(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
	}

	private static OrderResult MalformedResult()
	{
		return new OrderResult
		{
			Status = OrderStatus.Rejected,
			Errors = new[] { new ValidationError("$", "MALFORMED_INPUT", "Input must be a JSON object with compatible field types.") }
		};
	}
}
