namespace OrderIntake;

/// <summary>
/// A single validation failure: which field failed, a machine-readable code,
/// and a short human-readable message.
/// </summary>
public sealed class OrderError
{
    public string Field { get; }
    public string Code { get; }
    public string Message { get; }

    public OrderError(string field, string code, string message)
    {
        Field = field;
        Code = code;
        Message = message;
    }

    public override string ToString() => $"{Field}: {Code} - {Message}";
}
