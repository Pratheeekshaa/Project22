namespace OrderIntake;

/// <summary>
/// A fully validated laboratory order. Only ever constructed for an
/// Accepted result, so every property is guaranteed to satisfy the
/// validation rules in the spec.
/// </summary>
public sealed class LabOrder
{
    public string OrderId { get; }
    public string PatientId { get; }
    public string SpecimenId { get; }

    /// <summary>Normalized fixed form, e.g. "Blood".</summary>
    public string SpecimenType { get; }

    /// <summary>Normalized fixed form, e.g. "Urgent".</summary>
    public string Priority { get; }

    public DateOnly CollectionDate { get; }

    /// <summary>Requested tests, kept in the sender's original casing.</summary>
    public IReadOnlyList<string> RequestedTests { get; }

    public LabOrder(
        string orderId,
        string patientId,
        string specimenId,
        string specimenType,
        string priority,
        DateOnly collectionDate,
        IReadOnlyList<string> requestedTests)
    {
        OrderId = orderId;
        PatientId = patientId;
        SpecimenId = specimenId;
        SpecimenType = specimenType;
        Priority = priority;
        CollectionDate = collectionDate;
        RequestedTests = requestedTests;
    }
}
