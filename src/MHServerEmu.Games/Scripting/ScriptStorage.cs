using MHServerEmu.Core.Helpers;
using MHServerEmu.Core.Logging;

namespace MHServerEmu.Games.Scripting
{
    /// <summary>
    /// Small key / value files for scripts, so their state survives server restarts and script reloads (event progress,
    /// per-player counters). Each store is one text file in Data/ScriptData. Thread safe.
    /// </summary>
    /// <remarks>
    /// A store is read and written whole: keep it to a few thousand short entries and save every minute or so, not on
    /// every change. Keys and values must not contain tabs or line breaks.
    /// </remarks>
    public static class ScriptStorage
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private static readonly string Directory = Path.Combine(FileHelper.DataDirectory, "ScriptData");
        private static readonly object Lock = new();

        /// <summary>
        /// Loads the store called <paramref name="name"/>. Returns an empty dictionary if it does not exist yet.
        /// </summary>
        public static Dictionary<string, string> Load(string name)
        {
            Dictionary<string, string> data = new();

            string path = GetPath(name);
            if (path == null)
                return data;

            lock (Lock)
            {
                try
                {
                    if (File.Exists(path) == false)
                        return data;

                    foreach (string line in File.ReadAllLines(path))
                    {
                        int tab = line.IndexOf('\t');
                        if (tab > 0)
                            data[line[..tab]] = line[(tab + 1)..];
                    }
                }
                catch (Exception e)
                {
                    Logger.Warn($"Load(): Failed to read [{path}]: {e.Message}");
                }
            }

            return data;
        }

        /// <summary>
        /// Replaces the store called <paramref name="name"/> with <paramref name="data"/>. The file is written next to the
        /// old one and swapped in, so a crash mid-save never leaves a half-written store.
        /// </summary>
        public static bool Save(string name, IEnumerable<KeyValuePair<string, string>> data)
        {
            string path = GetPath(name);
            if (path == null || data == null)
                return false;

            lock (Lock)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(Directory);

                    string tempPath = path + ".tmp";
                    using (StreamWriter writer = new(tempPath, false))
                    {
                        foreach (var kvp in data)
                        {
                            if (string.IsNullOrEmpty(kvp.Key) || kvp.Key.Contains('\t') || kvp.Key.Contains('\n'))
                                continue;

                            string value = (kvp.Value ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
                            writer.Write(kvp.Key);
                            writer.Write('\t');
                            writer.WriteLine(value);
                        }
                    }

                    File.Move(tempPath, path, true);
                    return true;
                }
                catch (Exception e)
                {
                    return Logger.WarnReturn(false, $"Save(): Failed to write [{path}]: {e.Message}");
                }
            }
        }

        private static string GetPath(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return Logger.WarnReturn<string>(null, $"GetPath(): Invalid store name [{name}]");

            return Path.Combine(Directory, name + ".tsv");
        }
    }
}
