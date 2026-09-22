using Xunit;

namespace OrderIntake.Tests;

public class OrderIntakeServiceTests
{
    private readonly OrderIntakeService _service = new();

    /// <summary>
    /// Builds a valid order JSON string, letting callers override just the
    /// field(s) they care about for a given test.
    /// </summary>
    private static string BuildValidOrderJson(
        string orderId = "ORD-1",
        string patientId = "PAT-1",
        string specimenId = "SP-1",
        string specimenType = "Blood",
        string priority = "Routine",
        string? collectionDate = null,
        IEnumerable<string>? requestedTests = null)
    {
        collectionDate ??= DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        requestedTests ??= new[] { "Glucose" };

        var testsJson = string.Join(", ", requestedTests.Select(t => $"\"{t}\""));

        return $$"""
        {
          "orderId": "{{orderId}}",
          "patientId": "{{patientId}}",
          "specimenId": "{{specimenId}}",
          "specimenType": "{{specimenType}}",
          "priority": "{{priority}}",
          "collectionDate": "{{collectionDate}}",
          "requestedTests": [{{testsJson}}]
        }
        """;
    }

    // ------------------------------------------------------------------
    // Group 1: Accepted order
    // ------------------------------------------------------------------

    [Fact]
    public void AcceptedOrder_MixedCaseFieldsAndUnknownFields_NormalizesAndIgnoresUnknown()
    {
        var json = """
        {
          "orderId": "ORD-1005",
          "patientId": "PAT-505",
          "specimenId": "SP-9005",
          "specimenType": "BLOOD",
          "priority": "urgent",
          "collectionDate": "2026-09-18",
          "requestedTests": ["Glucose", "CompleteBloodCount"],
          "senderNote": "ignore me"
        }
        """;

        var result = _service.Process(json);

        Assert.Equal(OrderStatus.Accepted, result.Status);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Order);
        Assert.Equal("ORD-1005", result.Order!.OrderId);
        Assert.Equal("PAT-505", result.Order.PatientId);
        Assert.Equal("SP-9005", result.Order.SpecimenId);
        Assert.Equal("Blood", result.Order.SpecimenType);   // normalized from "BLOOD"
        Assert.Equal("Urgent", result.Order.Priority);       // normalized from "urgent"
        Assert.Equal(new DateOnly(2026, 9, 18), result.Order.CollectionDate);
        Assert.Equal(new[] { "Glucose", "CompleteBloodCount" }, result.Order.RequestedTests);
    }

    // ------------------------------------------------------------------
    // Group 2: All errors at once
    // ------------------------------------------------------------------

    [Fact]
    public void RejectedOrder_MultipleInvalidFields_ReturnsCompleteErrorSet()
    {
        var json = """
        {
          "orderId": "",
          "patientId": "PAT-1",
          "specimenId": "SP-1",
          "specimenType": "plasma",
          "priority": "asap",
          "collectionDate": "2026/09/20",
          "requestedTests": []
        }
        """;

        var result = _service.Process(json);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Null(result.Order);

        var actual = result.Errors.Select(e => (e.Field, e.Code)).ToHashSet();

        var expected = new HashSet<(string Field, string Code)>
        {
            ("orderId", "REQUIRED"),
            ("specimenType", "INVALID_VALUE"),
            ("priority", "INVALID_VALUE"),
            ("collectionDate", "INVALID_FORMAT"),
            ("requestedTests", "REQUIRED"),
        };

        Assert.Equal(expected, actual);
    }

    // ------------------------------------------------------------------
    // Group 3: ID length
    // ------------------------------------------------------------------

    [Fact]
    public void OrderId_ExactlyTwentyCharacters_IsAccepted()
    {
        var json = BuildValidOrderJson(orderId: new string('A', 20));

        var result = _service.Process(json);

        Assert.Equal(OrderStatus.Accepted, result.Status);
    }

    [Fact]
    public void OrderId_TwentyOneCharacters_ReturnsMaxLength()
    {
        var json = BuildValidOrderJson(orderId: new string('A', 21));

        var result = _service.Process(json);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Contains(result.Errors, e => e.Field == "orderId" && e.Code == "MAX_LENGTH");
    }

    // ------------------------------------------------------------------
    // Group 4: Collection date
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("2026-02-30")]   // not a real calendar date
    [InlineData("2026-9-2")]     // not zero-padded
    [InlineData("20-09-2026")]   // wrong shape / year width
    [InlineData("2026/09/20")]   // wrong separator
    public void CollectionDate_InvalidFormatOrCalendarDate_ReturnsInvalidFormat(string badDate)
    {
        var json = BuildValidOrderJson(collectionDate: badDate);

        var result = _service.Process(json);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Contains(result.Errors, e => e.Field == "collectionDate" && e.Code == "INVALID_FORMAT");
    }

    [Fact]
    public void CollectionDate_InTheFuture_ReturnsFutureDate()
    {
        // Calculated from today, never hardcoded.
        var future = DateOnly.FromDateTime(DateTime.Today).AddDays(1).ToString("yyyy-MM-dd");
        var json = BuildValidOrderJson(collectionDate: future);

        var result = _service.Process(json);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Contains(result.Errors, e => e.Field == "collectionDate" && e.Code == "FUTURE_DATE");
    }

    [Fact]
    public void CollectionDate_Today_IsAccepted()
    {
        var today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        var json = BuildValidOrderJson(collectionDate: today);

        var result = _service.Process(json);

        Assert.Equal(OrderStatus.Accepted, result.Status);
    }

    // ------------------------------------------------------------------
    // Group 5: Requested tests
    // ------------------------------------------------------------------

    [Fact]
    public void RequestedTests_EmptyList_ReturnsRequired()
    {
        var json = BuildValidOrderJson(requestedTests: Array.Empty<string>());

        var result = _service.Process(json);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Contains(result.Errors, e => e.Field == "requestedTests" && e.Code == "REQUIRED");
    }

    [Fact]
    public void RequestedTests_NamesDifferingOnlyByCase_ReturnsDuplicate()
    {
        var json = BuildValidOrderJson(requestedTests: new[] { "Glucose", "glucose" });

        var result = _service.Process(json);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Contains(result.Errors, e => e.Field == "requestedTests" && e.Code == "DUPLICATE");
    }

    // ------------------------------------------------------------------
    // Group 6: Broken JSON
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"just a string\"")]
    [InlineData("")]
    [InlineData("   ")]
    public void MalformedInput_ReturnsSingleMalformedInputError(string badJson)
    {
        var result = _service.Process(badJson);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Single(result.Errors);
        Assert.Equal("$", result.Errors[0].Field);
        Assert.Equal("MALFORMED_INPUT", result.Errors[0].Code);
    }

    [Fact]
    public void NullInput_ReturnsSingleMalformedInputError()
    {
        var result = _service.Process(null!);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Single(result.Errors);
        Assert.Equal("MALFORMED_INPUT", result.Errors[0].Code);
    }

    [Fact]
    public void IncompatibleFieldType_ReturnsSingleMalformedInputError()
    {
        var json = """{ "orderId": 123 }""";

        var result = _service.Process(json);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Single(result.Errors);
        Assert.Equal("MALFORMED_INPUT", result.Errors[0].Code);
    }

    [Fact]
    public void EmptyJsonObject_ReturnsFieldErrors_NotMalformed()
    {
        var result = _service.Process("{}");

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.DoesNotContain(result.Errors, e => e.Code == "MALFORMED_INPUT");
        Assert.Contains(result.Errors, e => e.Field == "orderId" && e.Code == "REQUIRED");
    }

    [Fact]
    public void Process_NeverThrows_OnSeverelyMalformedInput()
    {
        var exception = Record.Exception(() => _service.Process("not json at all {{{"));

        Assert.Null(exception);
    }

    // ------------------------------------------------------------------
    // Extra edge cases called out explicitly in the spec
    // ------------------------------------------------------------------

    [Fact]
    public void ExplicitNullField_ReturnsRequired_SameAsMissingField()
    {
        var json = """
        {
          "orderId": null,
          "patientId": "PAT-1",
          "specimenId": "SP-1",
          "specimenType": "Blood",
          "priority": "Routine",
          "collectionDate": "2026-01-01",
          "requestedTests": ["Glucose"]
        }
        """;

        var result = _service.Process(json);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Contains(result.Errors, e => e.Field == "orderId" && e.Code == "REQUIRED");
    }
}
