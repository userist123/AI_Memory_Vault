using System;
using System.Collections.Generic;

namespace LogAnalyzer.Core.Services.Edition
{
    /// <summary>
    /// The two applications that share the core libraries (owner decision 13).
    /// Classified (P1): a separate executable in which networking, AI, host-modifying actions and updates are not compiled in.
    /// Unclassified (P2/P3): one executable whose air-gapped / connected mode comes from a signed policy file.
    /// </summary>
    public enum EditionKind { Classified, Unclassified }

    /// <summary>Capabilities that exist only in the unclassified edition.</summary>
    public enum EditionFeature
    {
        /// <summary>Anything that opens a network connection or listens: syslog receiver, SIEM, online threat intel, Microsoft 365.</summary>
        NetworkConnectors,
        /// <summary>Real-time subscription to event logs (local or remote).</summary>
        LiveMonitoring,
        /// <summary>Active Directory (LDAP) and domain-controller log collection.</summary>
        DomainInvestigation,
        /// <summary>The optional AI explanation layer (local model over loopback).</summary>
        AiAnalysis,
        /// <summary>Actions that change the station: containment, host isolation, IoC blocking, audit policy changes, process suspension.</summary>
        HostResponse,
        /// <summary>Applying policies to the station (registry writes).</summary>
        PolicyApply,
        /// <summary>Running the audit collection script (PowerShell).</summary>
        AuditScriptCollection,
    }

    /// <summary>Which edition this process is, and which optional capabilities it contains.</summary>
    public interface IEditionProfile
    {
        EditionKind Kind { get; }
        string DisplayName { get; }
        /// <summary>True when the capability is compiled into this edition. Absent code is not "disabled": it is not there.</summary>
        bool Has(EditionFeature feature);
    }

    public static class EditionText
    {
        public const string NotAvailableClassified = "nu este disponibil în ediția clasificată";
        public static string Unavailable(string what) => $"{what}: {NotAvailableClassified}.";
    }

    /// <summary>P1: none of the optional capabilities exists unless an explicit compile-time option added it.</summary>
    public sealed class ClassifiedEditionProfile : IEditionProfile
    {
        private readonly HashSet<EditionFeature> _compiledIn;

        public ClassifiedEditionProfile(IEnumerable<EditionFeature>? compiledIn = null) =>
            _compiledIn = new HashSet<EditionFeature>(compiledIn ?? Array.Empty<EditionFeature>());

        public EditionKind Kind => EditionKind.Classified;
        public string DisplayName => "Ediția clasificată (P1)";
        public bool Has(EditionFeature feature) => _compiledIn.Contains(feature);
    }

    /// <summary>P2/P3: everything is compiled in; what may run is decided by NetworkPolicy and the signed policy.</summary>
    public sealed class UnclassifiedEditionProfile : IEditionProfile
    {
        public EditionKind Kind => EditionKind.Unclassified;
        public string DisplayName => "Ediția neclasificată (P2/P3)";
        public bool Has(EditionFeature feature) => true;
    }
}
