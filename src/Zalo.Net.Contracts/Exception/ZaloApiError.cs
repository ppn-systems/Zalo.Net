namespace Zalo.Net.Contracts.Errors;

/// <summary>
/// Exception thrown by Zalo API calls.
/// </summary>
public sealed class ZaloApiError : System.Exception
{
    /// <summary>
    /// Gets the Zalo API error code, if available.
    /// </summary>
    public int? Code { get; }

    public ZaloApiError(string message, int? code = null)
        : base(message) => this.Code = code;

    public ZaloApiError(string message, System.Exception innerException, int? code = null)
        : base(message, innerException) => this.Code = code;
}
