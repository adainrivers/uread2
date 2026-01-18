namespace URead2.Deserialization;

/// <summary>
/// A diagnostic message from deserialization.
/// </summary>
/// <param name="Code">The diagnostic code.</param>
/// <param name="Position">Stream position where the issue occurred.</param>
/// <param name="Detail">Optional detail message.</param>
public readonly record struct ReadDiagnostic(DiagnosticCode Code, long Position, string? Detail = null);
