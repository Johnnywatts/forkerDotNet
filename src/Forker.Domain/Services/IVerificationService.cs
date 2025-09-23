using Forker.Domain.Exceptions;

namespace Forker.Domain.Services;

/// <summary>
/// Service responsible for verifying the integrity of copied files by comparing hashes.
/// Enforces invariants I2, I5, I11, I15 related to verification and hash validation.
/// </summary>
public interface IVerificationService
{
    /// <summary>
    /// Verifies that the copied file matches the source hash.
    /// Enforces Invariant I15: Rehash matches original.
    /// </summary>
    /// <param name="filePath">Path to the file to verify</param>
    /// <param name="expectedHash">Expected SHA-256 hash from the source</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Verification result with computed hash and match status</returns>
    /// <exception cref="FileNotFoundException">When target file does not exist</exception>
    /// <exception cref="UnauthorizedAccessException">When file cannot be accessed</exception>
    /// <exception cref="OperationCanceledException">When operation is cancelled</exception>
    Task<VerificationResult> VerifyFileHashAsync(string filePath, string expectedHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a specific target outcome by computing its hash and comparing to source.
    /// Updates the target outcome state based on verification result.
    /// </summary>
    /// <param name="targetOutcome">Target outcome to verify</param>
    /// <param name="expectedHash">Expected SHA-256 hash from source file</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Verification result with detailed information</returns>
    Task<VerificationResult> VerifyTargetOutcomeAsync(TargetOutcome targetOutcome, string expectedHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch verification of multiple files with the same expected hash.
    /// Optimized for verifying multiple targets of the same source file.
    /// </summary>
    /// <param name="filePaths">Collection of file paths to verify</param>
    /// <param name="expectedHash">Expected SHA-256 hash</param>
    /// <param name="cancellationToken">Cancellation token for operation</param>
    /// <returns>Dictionary mapping file paths to their verification results</returns>
    Task<Dictionary<string, VerificationResult>> VerifyMultipleFilesAsync(
        IEnumerable<string> filePaths,
        string expectedHash,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a file verification operation containing hash comparison details.
/// </summary>
public sealed class VerificationResult
{
    /// <summary>
    /// SHA-256 hash computed from the target file.
    /// </summary>
    public string ComputedHash { get; }

    /// <summary>
    /// Expected SHA-256 hash from the source file.
    /// </summary>
    public string ExpectedHash { get; }

    /// <summary>
    /// True if computed hash matches expected hash.
    /// </summary>
    public bool IsMatch { get; }

    /// <summary>
    /// Size of the verified file in bytes.
    /// </summary>
    public long FileSize { get; }

    /// <summary>
    /// Duration taken to compute the hash and perform verification.
    /// </summary>
    public TimeSpan VerificationDuration { get; }

    /// <summary>
    /// File path that was verified.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Optional error message if verification failed due to I/O issues.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// True if verification completed successfully (regardless of hash match).
    /// False if verification failed due to I/O errors.
    /// </summary>
    public bool VerificationSucceeded { get; }

    /// <summary>
    /// Creates a successful verification result.
    /// </summary>
    public VerificationResult(string filePath, string computedHash, string expectedHash,
        long fileSize, TimeSpan verificationDuration)
    {
        FilePath = ValidateFilePath(filePath);
        ComputedHash = ValidateHash(computedHash, nameof(computedHash));
        ExpectedHash = ValidateHash(expectedHash, nameof(expectedHash));
        FileSize = ValidateFileSize(fileSize);
        VerificationDuration = verificationDuration;
        IsMatch = string.Equals(computedHash, expectedHash, StringComparison.OrdinalIgnoreCase);
        VerificationSucceeded = true;
        ErrorMessage = null;
    }

    /// <summary>
    /// Creates a failed verification result due to I/O error.
    /// </summary>
    public VerificationResult(string filePath, string expectedHash, string errorMessage)
    {
        FilePath = ValidateFilePath(filePath);
        ExpectedHash = ValidateHash(expectedHash, nameof(expectedHash));
        ErrorMessage = ValidateErrorMessage(errorMessage);
        ComputedHash = string.Empty;
        FileSize = 0;
        VerificationDuration = TimeSpan.Zero;
        IsMatch = false;
        VerificationSucceeded = false;
    }

    private static string ValidateFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null, empty, or whitespace.", nameof(filePath));
        return filePath.Trim();
    }

    private static string ValidateHash(string hash, string paramName)
    {
        if (string.IsNullOrWhiteSpace(hash))
            throw new ArgumentException("Hash cannot be null, empty, or whitespace.", paramName);
        return hash.Trim();
    }

    private static long ValidateFileSize(long fileSize)
    {
        if (fileSize < 0)
            throw new ArgumentOutOfRangeException(nameof(fileSize), fileSize, "File size cannot be negative.");
        return fileSize;
    }

    private static string ValidateErrorMessage(string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            throw new ArgumentException("Error message cannot be null, empty, or whitespace.", nameof(errorMessage));
        return errorMessage.Trim();
    }
}