namespace AeroFlow.BaggageReclaim.Auth;

/// <summary>Entra ID app roles this service checks (ADR-0018). Role names are the contract with the app registration.</summary>
public static class AuthRoles
{
    /// <summary>May complete a reclaim over HTTP (<c>POST /flights/{flightId}/reclaim/complete</c>).</summary>
    public const string ReclaimWrite = "Ops.Reclaim.Write";
}

/// <summary>Named authorisation policies used by controllers.</summary>
public static class AuthPolicies
{
    /// <summary>Requires <see cref="AuthRoles.ReclaimWrite"/>.</summary>
    public const string ReclaimWrite = "ReclaimWrite";
}
