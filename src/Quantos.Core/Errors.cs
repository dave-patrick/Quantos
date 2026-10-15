namespace Quantos.Core;

/// <summary>Base for errors the user or client can act on. <see cref="Code"/> is stable and safe to show.</summary>
public class QuantosException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class NotFoundException(string what) : QuantosException("not_found", $"{what} was not found.");

public sealed class ValidationException(string message) : QuantosException("invalid_request", message);

/// <summary>The file changed since the caller read it. Nothing was written.</summary>
public sealed class VersionConflictException(string nodeId, string expected, string current)
    : QuantosException("version_conflict", "The note changed since you opened it. Nothing was saved.")
{
    public string NodeId { get; } = nodeId;
    public string ExpectedVersion { get; } = expected;
    public string CurrentVersion { get; } = current;
}

public sealed class VaultLockedException(string vaultPath)
    : QuantosException("vault_in_use", $"Another Quantos backend is already using the vault at {vaultPath}. Connect to it instead of starting a second one.");
