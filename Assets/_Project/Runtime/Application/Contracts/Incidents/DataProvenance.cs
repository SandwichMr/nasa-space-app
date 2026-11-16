using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Trainer.Application.Outpost;

namespace Trainer.Application.Incidents
{
    /// <summary>Phase 5 bounds of the published incident state and of the data it carries (memory and payload).</summary>
    public static class IncidentLimits
    {
        public const int MaxIncidents = 8;
        public const int MaxDirectorDecisions = 16;
        public const int MaxEpisodeMessages = 8;
        public const int MaxSourceRecords = 12;
        public const int MaxMeasurements = 4;
        public const int MaxAssignments = 6;
        public const int MaxAssignmentTemplates = 8;
        public const int MaxCatalogEntries = 8;
        public const int MaxEpisodes = 4;
        public const int MaxOperations = 4;
        public const int MaxFeedback = 8;
        public const int MaxAssessments = OutpostLimits.MaxCrew;
        public const int MaxReceptions = 64;
        /// <summary>Longest free text kept from a source (summary, unit, location): bounded and never executed.</summary>
        public const int MaxTextLength = 280;
        public const int MaxIdLength = 64;
        public const int MaxCredits = 12;
    }

    /// <summary>Where the space-weather information of a session comes from. Three clearly different modes.</summary>
    public enum DataMode
    {
        /// <summary>A real NASA/CCMC DONKI episode, frozen and shipped with the build (offline replay with provenance).</summary>
        Historical,
        /// <summary>An authored, seeded TRAINING generator: labeled synthetic, not an observation.</summary>
        Synthetic,
        /// <summary>Current observation. Not implemented in phase 5: the type exists so a cable can reject it explicitly.</summary>
        Current
    }

    /// <summary>What the base actually uses after loading the dataset (a failed load never means "no hazard").</summary>
    public enum DataAvailability
    {
        /// <summary>The requested dataset loaded and validated.</summary>
        Loaded,
        /// <summary>The historical dataset was missing, corrupt or of an unknown schema: the labeled synthetic generator is used.</summary>
        FallbackSynthetic,
        /// <summary>Synthetic requested: no external data involved.</summary>
        NotRequested
    }

    /// <summary>DONKI record families used in phase 5 (no others are parsed). Flare, CME and SEP are different phenomena.</summary>
    public enum SourceRecordKind
    {
        Flare,
        CoronalMassEjection,
        SolarEnergeticParticles,
        Notification
    }

    /// <summary>Evidence class of every value shown or used (SCIENTIFIC_RULES: observation / reference / calculation / assumption).</summary>
    public enum ValueClassification
    {
        Observation,
        Reference,
        Calculation,
        Assumption
    }

    /// <summary>One quantity quoted from a source record with its unit and where it was measured. A missing value is null, never 0.</summary>
    public sealed class SourceMeasurement
    {
        public string Quantity { get; }
        public double? Value { get; }
        public string Unit { get; }
        public string Location { get; }
        public ValueClassification Classification { get; }

        public SourceMeasurement(string quantity, double? value, string unit, string location, ValueClassification classification)
        {
            Quantity = ProvenanceText.Required(quantity, nameof(quantity));
            if (value.HasValue && !OutpostGuard.IsFinite(value.Value)) throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
            Unit = ProvenanceText.Required(unit, nameof(unit));
            Location = ProvenanceText.Required(location, nameof(location));
            Classification = OutpostGuard.Defined(classification, nameof(classification));
        }
    }

    /// <summary>
    /// One original record as kept in the frozen cache (immutable). Times are ISO-8601 UTC strings exactly as the provider wrote
    /// them, or null when unknown. <see cref="EventTimeUtc"/> = when the phenomenon was observed to start; <see cref="IssuedUtc"/>
    /// = message issue time (notifications) or submission time (catalog entries) — the first availability we can DEMONSTRATE
    /// for that record, which is not a universal first publication. <see cref="RetrievedUtc"/> = our download.
    /// </summary>
    public sealed class SourceRecord
    {
        public string Provider { get; }
        public SourceRecordKind Kind { get; }
        public string SourceId { get; }
        public int? Version { get; }
        public string Uri { get; }
        public string EventTimeUtc { get; }
        public string IssuedUtc { get; }
        public string RetrievedUtc { get; }
        public string Summary { get; }
        public IReadOnlyList<SourceMeasurement> Measurements { get; }

        public SourceRecord(string provider, SourceRecordKind kind, string sourceId, int? version, string uri, string eventTimeUtc,
            string issuedUtc, string retrievedUtc, string summary, IEnumerable<SourceMeasurement> measurements)
        {
            Provider = ProvenanceText.Required(provider, nameof(provider));
            Kind = OutpostGuard.Defined(kind, nameof(kind));
            SourceId = ProvenanceText.Id(sourceId, nameof(sourceId));
            if (version.HasValue && version.Value < 0) throw new ArgumentOutOfRangeException(nameof(version));
            Version = version;
            Uri = ProvenanceText.Required(uri, nameof(uri));
            EventTimeUtc = ProvenanceText.OptionalUtc(eventTimeUtc, nameof(eventTimeUtc));
            IssuedUtc = ProvenanceText.OptionalUtc(issuedUtc, nameof(issuedUtc));
            RetrievedUtc = ProvenanceText.OptionalUtc(retrievedUtc, nameof(retrievedUtc));
            Summary = ProvenanceText.Required(summary, nameof(summary));
            var list = new List<SourceMeasurement>(measurements ?? Array.Empty<SourceMeasurement>());
            if (list.Count > IncidentLimits.MaxMeasurements || list.Contains(null)) throw new ArgumentException("Too many or null measurements.", nameof(measurements));
            Measurements = new ReadOnlyCollection<SourceMeasurement>(list);
        }
    }

    /// <summary>Identity of the dataset a session uses (immutable): enough to reproduce and audit it.</summary>
    public sealed class DatasetManifest
    {
        public string DatasetId { get; }
        public DataMode Mode { get; }
        public int SchemaVersion { get; }
        /// <summary>SHA-256 (hex) of the normalized dataset bytes the build shipped, or null for the synthetic generator.</summary>
        public string Sha256 { get; }
        public string RetrievedUtc { get; }
        public string License { get; }

        public DatasetManifest(string datasetId, DataMode mode, int schemaVersion, string sha256, string retrievedUtc, string license)
        {
            DatasetId = ProvenanceText.Id(datasetId, nameof(datasetId));
            Mode = OutpostGuard.Defined(mode, nameof(mode));
            if (schemaVersion < 1) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            SchemaVersion = schemaVersion;
            if (sha256 != null && (sha256.Length != 64 || !IsHex(sha256))) throw new ArgumentException("SHA-256 hex expected.", nameof(sha256));
            if (mode == DataMode.Historical && sha256 == null) throw new ArgumentException("A historical dataset needs its hash.", nameof(sha256));
            Sha256 = sha256;
            RetrievedUtc = ProvenanceText.OptionalUtc(retrievedUtc, nameof(retrievedUtc));
            License = ProvenanceText.Required(license, nameof(license));
        }

        private static bool IsHex(string value)
        {
            foreach (char c in value)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }
    }

    internal static class ProvenanceText
    {
        public static string Required(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > IncidentLimits.MaxTextLength)
                throw new ArgumentException($"Text of 1..{IncidentLimits.MaxTextLength} characters required.", name);
            foreach (char c in value)
                if (char.IsControl(c) && c != '\n') throw new ArgumentException("Control characters are not allowed.", name);
            return value;
        }

        /// <summary>Provider ids (e.g. 2017-09-10T16:25:00-SEP-001): printable ASCII without spaces, bounded.</summary>
        public static string Id(string value, string name)
        {
            if (string.IsNullOrEmpty(value) || value.Length > IncidentLimits.MaxIdLength) throw new ArgumentException("Id required.", name);
            foreach (char c in value)
                if (c <= ' ' || c > '~') throw new ArgumentException("Printable ASCII id expected.", name);
            return value;
        }

        /// <summary>Null (unknown) or an ISO-8601 UTC instant ending in Z, 17..24 characters (e.g. 2017-09-10T16:41Z).</summary>
        public static string OptionalUtc(string value, string name)
        {
            if (value == null) return null;
            if (value.Length < 17 || value.Length > 24 || value[4] != '-' || value[10] != 'T' || value[value.Length - 1] != 'Z')
                throw new ArgumentException($"'{value}' is not an ISO-8601 UTC instant.", name);
            foreach (char c in value)
                if (!((c >= '0' && c <= '9') || c == '-' || c == ':' || c == 'T' || c == 'Z' || c == '.'))
                    throw new ArgumentException($"'{value}' is not an ISO-8601 UTC instant.", name);
            return value;
        }
    }
}
