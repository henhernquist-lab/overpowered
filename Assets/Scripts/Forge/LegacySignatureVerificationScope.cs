#if UNITY_EDITOR
using System;
using UnityEngine;

/// Explicit legacy fallback fixture. Shipping/default signature coverage lives in the signature suites.
/// Only mutates the in-memory gate; legacy definitions, selection/resource/ownership gates and damage remain real.
public sealed class LegacySignatureVerificationScope : IDisposable
{
    readonly SignatureCatalog catalog;
    readonly bool complete, preview, verification;
    public LegacySignatureVerificationScope()
    {
        catalog = Resources.Load<SignatureCatalog>("SignatureCatalog");
        complete = catalog.EnableWhenComplete; preview = catalog.DeveloperPreview; verification = SignatureCatalog.VerificationPreview;
        catalog.EnableWhenComplete = false; catalog.DeveloperPreview = false; SignatureCatalog.VerificationPreview = false;
        Debug.Log("LEGACY FIXTURE: signature gate temporarily off in memory; testing retained legacy fallback, not the default roster.");
    }
    public void Dispose()
    {
        if (catalog != null) { catalog.EnableWhenComplete = complete; catalog.DeveloperPreview = preview; }
        SignatureCatalog.VerificationPreview = verification;
    }
}
#endif
