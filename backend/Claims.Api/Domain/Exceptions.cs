namespace Claims.Api.Domain;

/// <summary>Input breaks a business rule (maps to HTTP 400).</summary>
public class DomainValidationException(string message) : Exception(message);

/// <summary>The operation is not valid for the claim's current state (maps to HTTP 409).</summary>
public class ClaimStateException(string message, string code = "invalid_state") : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>The caller is authenticated but may not perform this action on this claim (maps to HTTP 403).</summary>
public class ClaimAccessException(string message) : Exception(message);

/// <summary>The resource does not exist or is not visible to the caller (maps to HTTP 404).</summary>
public class NotFoundException(string message) : Exception(message);
