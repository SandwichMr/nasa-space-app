using System;
using System.IO;
using Trainer.Application.Incidents;

namespace Trainer.Unity.Integration.SpaceWeather
{
    /// <summary>
    /// Authority-side adapter that reads the frozen DONKI dataset (manifest.json + episode.json) from a folder and hands the bytes to
    /// the pure <see cref="HistoricalEpisodeLoader"/> (D2/D3). Offline only: it never contacts a network service. Any read problem
    /// (missing folder, missing file, oversize, I/O error) becomes the loader's stable problem key, and the session then plays the
    /// labelled synthetic fallback. Clients never read it: they receive what the authority publishes.
    /// </summary>
    public static class HistoricalDatasetSource
    {
        public const string ManifestFile = "manifest.json";
        public const string EpisodeFile = "episode.json";

        /// <summary>Loads from <paramref name="folder"/> (absolute, or relative to <paramref name="streamingAssetsPath"/>).</summary>
        public static bool TryLoad(string streamingAssetsPath, string folder, out SpaceWeatherEpisodeDefinition episode, out string problemKey)
        {
            episode = null;
            if (string.IsNullOrWhiteSpace(folder))
            {
                problemKey = HistoricalEpisodeLoader.Missing;
                return false;
            }
            string path = Path.IsPathRooted(folder) ? folder : Path.Combine(streamingAssetsPath ?? string.Empty, folder);
            byte[] manifest = Read(Path.Combine(path, ManifestFile));
            byte[] data = Read(Path.Combine(path, EpisodeFile));
            return HistoricalEpisodeLoader.TryLoad(manifest, data, out episode, out problemKey);
        }

        private static byte[] Read(string file)
        {
            try
            {
                var info = new FileInfo(file);
                if (!info.Exists) return null;
                if (info.Length > HistoricalEpisodeLoader.MaxFileBytes) return new byte[HistoricalEpisodeLoader.MaxFileBytes + 1];
                return File.ReadAllBytes(file);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is ArgumentException)
            {
                return null;
            }
        }
    }
}
