namespace OrderIntake;

/// <summary>
/// The single result object returned by OrderIntakeService.Process.
/// Accepted: Order is set, Errors is empty.
/// Rejected: Order is null, Errors holds every rule that failed
/// (or exactly one MALFORMED_INPUT error for unreadable input).
/// </summary>
public sealed class OrderResult
{
    public OrderStatus Status { get; }
    public LabOrder? Order { get; }
    public IReadOnlyList<OrderError> Errors { get; }

    private OrderResult(OrderStatus status, LabOrder? order, IReadOnlyList<OrderError> errors)
    {
        Status = status;
        Order = order;
        Errors = errors;
    }

    public static OrderResult Accepted(LabOrder order) =>
        new(OrderStatus.Accepted, order, Array.Empty<OrderError>());

    public static OrderResult Rejected(IReadOnlyList<OrderError> errors) =>
        new(OrderStatus.Rejected, null, errors);
}
