// Copyright (c) 2026 PPN Corporation. All rights reserved.
// Licensed under the Apache License, Version 2.0.

namespace Zalo.Net.Contracts.Exceptions;

/// <summary>
/// Exception thrown by Zalo API operations.
/// </summary>
public class ZaloApiException : System.Exception
{
    /// <summary>
    /// Gets the Zalo API error code, if available.
    /// </summary>
    public int? Code { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ZaloApiException"/> class with a message and error code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="code">The optional Zalo API error code.</param>
    public ZaloApiException(string message, int? code = null)
        : base(message) => this.Code = code;

    /// <summary>
    /// Initializes a new instance of the <see cref="ZaloApiException"/> class with a message, inner exception, and error code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    /// <param name="code">The optional Zalo API error code.</param>
    public ZaloApiException(string message, System.Exception innerException, int? code = null)
        : base(message, innerException) => this.Code = code;
}
