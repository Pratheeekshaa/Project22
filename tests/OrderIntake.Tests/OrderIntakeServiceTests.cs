using OrderIntake;

namespace OrderIntake.Tests;

public class OrderIntakeServiceTests
{
    private readonly OrderIntakeService service = new();

    [Fact]
    public void AcceptsOrderAndNormalizesValuesWhileIgnoringUnknownFields()
    {
        OrderResult result = service.Process("""
            {
              "orderId": "ORD-1005",
              "patientId": "PAT-505",
              "specimenId": "SP-9005",
              "specimenType": "bLoOd",
              "priority": "uRgEnT",
              "collectionDate": "2026-09-18",
              "requestedTests": ["Glucose", "CompleteBloodCount"],
              "senderNote": "ignore me"
            }
            """);

        Assert.Equal(OrderStatus.Accepted, result.Status);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Order);
        Assert.Equal("ORD-1005", result.Order.OrderId);
        Assert.Equal("PAT-505", result.Order.PatientId);
        Assert.Equal("SP-9005", result.Order.SpecimenId);
        Assert.Equal("Blood", result.Order.SpecimenType);
        Assert.Equal("Urgent", result.Order.Priority);
        Assert.Equal(new DateOnly(2026, 9, 18), result.Order.CollectionDate);
        Assert.Equal(new[] { "Glucose", "CompleteBloodCount" }, result.Order.RequestedTests);
    }

    [Fact]
    public void ReturnsAllFieldErrors()
    {
        OrderResult result = service.Process("""
            {
              "orderId": " ",
              "patientId": "PAT-505",
              "specimenId": "SP-9005",
              "specimenType": "plasma",
              "priority": "stat",
              "collectionDate": "2026-02-30",
              "requestedTests": []
            }
            """);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Null(result.Order);
        Assert.Equal(
            new[]
            {
                ("orderId", "REQUIRED"),
                ("specimenType", "INVALID_VALUE"),
                ("priority", "INVALID_VALUE"),
                ("collectionDate", "INVALID_FORMAT"),
                ("requestedTests", "INVALID_VALUE")
            },
            result.Errors.Select(error => (error.Field, error.Code)));
    }

    [Theory]
    [InlineData(20, OrderStatus.Accepted, null)]
    [InlineData(21, OrderStatus.Rejected, "MAX_LENGTH")]
    public void ValidatesIdLength(int length, OrderStatus expectedStatus, string? expectedCode)
    {
        string orderId = new('A', length);
        OrderResult result = service.Process(ValidOrder(orderId: orderId));

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedCode, result.Errors.SingleOrDefault()?.Code);
    }

    [Theory]
    [InlineData("2026-02-30")]
    [InlineData("2026-9-2")]
    [InlineData("20-09-2026")]
    [InlineData("2026/09/20")]
    public void RejectsInvalidCollectionDates(string date)
    {
        OrderResult result = service.Process(ValidOrder(collectionDate: date));

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Equal("INVALID_FORMAT", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void RejectsFutureCollectionDate()
    {
        string tomorrow = DateOnly.FromDateTime(DateTime.Today.AddDays(1)).ToString("yyyy-MM-dd");
        OrderResult result = service.Process(ValidOrder(collectionDate: tomorrow));

        Assert.Equal("FUTURE_DATE", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public void RejectsEmptyTestsAndCaseInsensitiveDuplicates()
    {
        OrderResult empty = service.Process(ValidOrder(requestedTests: "[]"));
        OrderResult duplicate = service.Process(ValidOrder(requestedTests: "[\"Glucose\", \"glucose\"]"));

        Assert.Equal(("requestedTests", "INVALID_VALUE"), (Assert.Single(empty.Errors).Field, empty.Errors.Single().Code));
        Assert.Equal(("requestedTests", "DUPLICATE"), (Assert.Single(duplicate.Errors).Field, duplicate.Errors.Single().Code));
    }

    [Theory]
    [InlineData("{\"orderId\": ")]
    [InlineData("[]")]
    [InlineData("\"order\"")]
    [InlineData("{\"orderId\":123}")]
    [InlineData("{\"requestedTests\":[1]}")]
    public void RejectsMalformedOrWrongShapeInputWithOneError(string json)
    {
        OrderResult result = service.Process(json);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Null(result.Order);
        var error = Assert.Single(result.Errors);
        Assert.Equal("$", error.Field);
        Assert.Equal("MALFORMED_INPUT", error.Code);
    }

    [Fact]
    public void EmptyObjectGetsNormalFieldErrors()
    {
        OrderResult result = service.Process("{}");

        Assert.Equal(7, result.Errors.Count);
        Assert.All(result.Errors, error => Assert.Equal("REQUIRED", error.Code));
    }

    private static string ValidOrder(string orderId = "ORD-1", string collectionDate = "2026-09-18", string requestedTests = "[\"Glucose\"]") =>
        $"{{\"orderId\":\"{orderId}\",\"patientId\":\"PAT-1\",\"specimenId\":\"SP-1\",\"specimenType\":\"blood\",\"priority\":\"routine\",\"collectionDate\":\"{collectionDate}\",\"requestedTests\":{requestedTests}}}";
}