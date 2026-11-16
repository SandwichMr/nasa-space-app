using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Trainer.Application.Incidents
{
    /// <summary>
    /// Loads the frozen historical episode (schema v1, written by tools/data-pipeline/phase05/normalize_episode.py) from the bytes
    /// of its manifest and episode files (D2/D3). Pure C#: the Unity adapter only reads the files. Checks, in order: presence,
    /// manifest shape, SHA-256 of the episode bytes against the manifest, schema version, and finally every contract rule (the
    /// contract constructors). Any failure returns a stable problem key and NO episode: the session then plays labelled synthetic
    /// episodes (fallback), never "no hazard". Nulls stay null (unknown is not zero).
    /// </summary>
    public static class HistoricalEpisodeLoader
    {
        public const int SupportedSchemaVersion = 1;
        public const string Missing = "dataset-missing";
        public const string ManifestInvalid = "dataset-manifest-invalid";
        public const string HashMismatch = "dataset-hash-mismatch";
        public const string SchemaUnsupported = "dataset-schema-unsupported";
        public const string Invalid = "dataset-invalid";
        public const int MaxFileBytes = 256 * 1024;

        public static bool TryLoad(byte[] manifestBytes, byte[] episodeBytes, out SpaceWeatherEpisodeDefinition episode, out string problemKey)
        {
            episode = null;
            if (manifestBytes == null || episodeBytes == null || manifestBytes.Length == 0 || episodeBytes.Length == 0)
                return Fail(Missing, out problemKey);
            if (manifestBytes.Length > MaxFileBytes || episodeBytes.Length > MaxFileBytes) return Fail(Invalid, out problemKey);

            Dictionary<string, object> manifest;
            try
            {
                manifest = MiniJson.Parse(Encoding.UTF8.GetString(manifestBytes)) as Dictionary<string, object>;
            }
            catch (FormatException)
            {
                manifest = null;
            }
            if (manifest == null || !(manifest.TryGetValue("sha256", out object hashValue) && hashValue is string expected)
                || expected.Length != 64)
                return Fail(ManifestInvalid, out problemKey);
            if (!string.Equals(LivingBaseIds.Sha256Hex(episodeBytes), expected, StringComparison.Ordinal)) return Fail(HashMismatch, out problemKey);
            if (Number(manifest, "schemaVersion") != SupportedSchemaVersion) return Fail(SchemaUnsupported, out problemKey);

            try
            {
                var root = MiniJson.Parse(Encoding.UTF8.GetString(episodeBytes)) as Dictionary<string, object>;
                if (root == null || Number(root, "schemaVersion") != SupportedSchemaVersion) return Fail(SchemaUnsupported, out problemKey);
                episode = Build(root, expected);
                problemKey = null;
                return true;
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException || e is InvalidCastException || e is KeyNotFoundException)
            {
                episode = null;
                return Fail(Invalid, out problemKey);
            }
        }

        private static bool Fail(string key, out string problemKey)
        {
            problemKey = key;
            return false;
        }

        private static SpaceWeatherEpisodeDefinition Build(Dictionary<string, object> root, string sha256)
        {
            if (Text(root, "mode") != "Historical") throw new FormatException("Only historical episodes are loaded from data.");
            var d = (Dictionary<string, object>)root["dataset"];
            var dataset = new DatasetManifest(Text(d, "datasetId"), DataMode.Historical, (int)Number(d, "schemaVersion"), sha256,
                Text(d, "retrievedUtc"), Text(d, "license"));
            var records = new List<SourceRecord>();
            foreach (object item in (List<object>)root["records"])
            {
                var r = (Dictionary<string, object>)item;
                var measurements = new List<SourceMeasurement>();
                foreach (object mi in (List<object>)r["measurements"])
                {
                    var m = (Dictionary<string, object>)mi;
                    measurements.Add(new SourceMeasurement(Text(m, "quantity"), OptionalNumber(m, "value"), Text(m, "unit"), Text(m, "location"),
                        Enum<ValueClassification>(m, "classification")));
                }
                double? version = OptionalNumber(r, "version");
                records.Add(new SourceRecord(Text(r, "provider"), Enum<SourceRecordKind>(r, "kind"), Text(r, "sourceId"),
                    version.HasValue ? (int?)(int)version.Value : null, Text(r, "uri"), OptionalText(r, "eventTimeUtc"), OptionalText(r, "issuedUtc"),
                    OptionalText(r, "retrievedUtc"), Text(r, "summary"), measurements));
            }
            var messages = new List<EpisodeMessage>();
            foreach (object item in (List<object>)root["messages"])
            {
                var m = (Dictionary<string, object>)item;
                var sources = new List<string>();
                foreach (object s in (List<object>)m["sourceRecordIds"]) sources.Add((string)s);
                messages.Add(new EpisodeMessage(Text(m, "id"), Enum<EpisodeMessageKind>(m, "kind"), Enum<MessageBasis>(m, "basis"),
                    Number(m, "offsetSeconds"), Number(m, "validForSeconds"), OptionalNumber(m, "shelterByOffsetSeconds"), OptionalText(m, "supersedes"),
                    Text(m, "textKey"), sources, OptionalText(m, "sourceIssuedUtc")));
            }
            return new SpaceWeatherEpisodeDefinition(Text(root, "key"), DataMode.Historical, Text(root, "label"), Text(root, "provenance"),
                Text(root, "timelineNote"), dataset, records, messages, Number(root, "didacticOnsetSeconds"), Number(root, "didacticEndSeconds"));
        }

        private static string Text(Dictionary<string, object> o, string key) =>
            o.TryGetValue(key, out object v) && v is string s ? s : throw new FormatException("Missing text '" + key + "'.");

        private static string OptionalText(Dictionary<string, object> o, string key) =>
            !o.TryGetValue(key, out object v) || v == null ? null : v as string ?? throw new FormatException("Bad text '" + key + "'.");

        private static double Number(Dictionary<string, object> o, string key) =>
            o.TryGetValue(key, out object v) && v is double d ? d : throw new FormatException("Missing number '" + key + "'.");

        private static double? OptionalNumber(Dictionary<string, object> o, string key) =>
            !o.TryGetValue(key, out object v) || v == null ? (double?)null : v is double d ? d : throw new FormatException("Bad number '" + key + "'.");

        private static T Enum<T>(Dictionary<string, object> o, string key) where T : struct
        {
            string text = Text(o, key);
            if (!System.Enum.TryParse(text, false, out T value) || !System.Enum.IsDefined(typeof(T), value) || char.IsDigit(text[0]))
                throw new FormatException("Unknown " + typeof(T).Name + " '" + text + "'.");
            return value;
        }
    }

    /// <summary>Minimal strict JSON reader (objects, arrays, strings, numbers, true/false/null) for the frozen dataset. Throws
    /// <see cref="FormatException"/> on any malformed input; depth and size are bounded by the caller's file limit.</summary>
    internal static class MiniJson
    {
        private const int MaxDepth = 32;

        public static object Parse(string text)
        {
            int i = 0;
            object value = ReadValue(text, ref i, 0);
            SkipWhitespace(text, ref i);
            if (i != text.Length) throw new FormatException("Trailing data.");
            return value;
        }

        private static object ReadValue(string s, ref int i, int depth)
        {
            if (depth > MaxDepth) throw new FormatException("Too deep.");
            SkipWhitespace(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end.");
            char c = s[i];
            switch (c)
            {
                case '{':
                {
                    var o = new Dictionary<string, object>(StringComparer.Ordinal);
                    i++;
                    SkipWhitespace(s, ref i);
                    if (i < s.Length && s[i] == '}') { i++; return o; }
                    while (true)
                    {
                        SkipWhitespace(s, ref i);
                        string key = ReadString(s, ref i);
                        SkipWhitespace(s, ref i);
                        Expect(s, ref i, ':');
                        object v = ReadValue(s, ref i, depth + 1);
                        if (o.ContainsKey(key)) throw new FormatException("Duplicate key.");
                        o.Add(key, v);
                        SkipWhitespace(s, ref i);
                        if (i < s.Length && s[i] == ',') { i++; continue; }
                        Expect(s, ref i, '}');
                        return o;
                    }
                }
                case '[':
                {
                    var a = new List<object>();
                    i++;
                    SkipWhitespace(s, ref i);
                    if (i < s.Length && s[i] == ']') { i++; return a; }
                    while (true)
                    {
                        a.Add(ReadValue(s, ref i, depth + 1));
                        SkipWhitespace(s, ref i);
                        if (i < s.Length && s[i] == ',') { i++; continue; }
                        Expect(s, ref i, ']');
                        return a;
                    }
                }
                case '"':
                    return ReadString(s, ref i);
                case 't': Literal(s, ref i, "true"); return true;
                case 'f': Literal(s, ref i, "false"); return false;
                case 'n': Literal(s, ref i, "null"); return null;
                default:
                    return ReadNumber(s, ref i);
            }
        }

        private static string ReadString(string s, ref int i)
        {
            Expect(s, ref i, '"');
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("Unterminated string.");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c < ' ') throw new FormatException("Control character in string.");
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("Bad escape.");
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length || !ushort.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort code))
                            throw new FormatException("Bad unicode escape.");
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default: throw new FormatException("Bad escape.");
                }
            }
        }

        private static double ReadNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == start) throw new FormatException("Unexpected character.");
            if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                || double.IsNaN(v) || double.IsInfinity(v))
                throw new FormatException("Bad number.");
            return v;
        }

        private static void Literal(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw new FormatException("Bad literal.");
            i += word.Length;
        }

        private static void Expect(string s, ref int i, char c)
        {
            if (i >= s.Length || s[i] != c) throw new FormatException("Expected '" + c + "'.");
            i++;
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }
    }
}
