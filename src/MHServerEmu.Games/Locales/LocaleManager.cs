using System.Text;
using MHServerEmu.Core.Helpers;
using MHServerEmu.Core.Logging;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Calligraphy;

namespace MHServerEmu.Games.Locales
{
    /// <summary>
    /// A singleton that manages <see cref="Locale"/> instances.
    /// </summary>
    public class LocaleManager
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private readonly List<Locale> _locales = new();

        public static LocaleManager Instance { get; } = new();

        public Locale CurrentLocale { get; private set; }

        private LocaleManager() { }

        public bool Initialize(bool loadLocaleFiles)
        {
            if (loadLocaleFiles)
            {
                string localeDirectory = Path.Combine(FileHelper.DataDirectory, "Game", "Loco");
                if (Directory.Exists(localeDirectory))
                {
                    foreach (string filePath in Directory.GetFiles(localeDirectory, "*.locale"))
                        LoadLocaleFile(filePath);
                }
            }

            if (_locales.Count == 0)
            {
                // We don't need full locale support server-side, so we can just initialize a default locale in memory.
                Locale defaultLocale = new(this, "Data/Game/Loco/eng.all.locale", "English", LocaleLanguage.English, "English", LocaleRegion.All, "Everywhere", "eng.all");
                _locales.Add(defaultLocale);
            }

            Locale locale = GetLocale(LocaleLanguage.English, LocaleRegion.All);
            if (!Verify.IsNotNull(locale)) return false;

            SetCurrentLocale(locale);

            return true;
        }

        public Locale GetLocale(LocaleLanguage language, LocaleRegion region)
        {
            foreach (Locale locale in _locales)
            {
                if (locale.Language == language && locale.Region == region)
                    return locale;
            }

            return null;
        }

        public bool SetCurrentLocale(Locale locale)
        {
            if (CurrentLocale == locale)
                return true;

            CurrentLocale?.OnUnsetCurrent();

            CurrentLocale = locale;

            CurrentLocale?.OnSetCurrent();
            // Skipping Formatter initialization here

            Logger.Info($"Current locale set to {locale}");
            return true;
        }

        public static LocaleLanguage GetLanguageFromDirectory(string directory)
        {
            ReadOnlySpan<char> prefix = directory != null && directory.Length >= 3 ? directory.AsSpan()[..3] : ReadOnlySpan<char>.Empty;

            switch (prefix)
            {
                case "chi": return LocaleLanguage.Chinese;
                case "eng": return LocaleLanguage.English;
                case "fra": return LocaleLanguage.French;
                case "deu": return LocaleLanguage.German;
                case "por": return LocaleLanguage.Portuguese;
                case "spa": return LocaleLanguage.Spanish;
                case "rus": return LocaleLanguage.Russian;
                case "jpn": return LocaleLanguage.Japanese;
                case "kor": return LocaleLanguage.Korean;
                case "sg1": return LocaleLanguage.PigLatin;
                case "sg2": return LocaleLanguage.GreekCypher;
                case "sg3": return LocaleLanguage.Placeholder;

                default:
                    Verify.IsTrue(false, $"Unhandled language in locale directory: {directory}");
                    return LocaleLanguage.Invalid;
            }
        }

        public static LocaleRegion GetRegionFromDirectory(string directory)
        {
            if (directory.EndsWith("all"))
                return LocaleRegion.All;

            Verify.IsTrue(false, $"Unhandled region in locale directory: {directory}");
            return LocaleRegion.Invalid;
        }

        private bool LoadLocaleFile(string filePath)
        {
            if (!Verify.IsTrue(ReadLocaleFile(filePath, out string name, out string languageDisplayName, out string regionDisplayName, out string directory, out LocaleFlag[] flags)))
                return false;

            LocaleLanguage language = GetLanguageFromDirectory(directory);
            LocaleRegion region = GetRegionFromDirectory(directory);

            Locale locale = new(this, filePath, name, language, languageDisplayName, region, regionDisplayName, directory);
            if (!Verify.IsTrue(locale.Initialize(flags))) return false;

            _locales.Add(locale);

            Logger.Info($"Loaded locale file {Path.GetFileNameWithoutExtension(filePath)}");
            return true;
        }

        private bool ReadLocaleFile(string filePath, out string name, out string languageDisplayName, out string regionDisplayName, out string directory, out LocaleFlag[] flags)
        {
            name = default;
            languageDisplayName = default;
            regionDisplayName = default;
            directory = default;
            flags = default;

            using FileStream fileStream = File.OpenRead(filePath);

            using CalligraphyReader reader = new(fileStream);   // BinaryReader client-side

            // The client doesn't treat this as a Calligraphy header, but it follows the same format.
            if (!Verify.IsTrue(reader.ReadHeader("LOC", DataDirectory.CalligraphyStringVersion))) return false;

            bool success = ReadString(reader, filePath, fileStream, out name) &&
                           ReadString(reader, filePath, fileStream, out languageDisplayName) &&
                           ReadString(reader, filePath, fileStream, out regionDisplayName) &&
                           ReadString(reader, filePath, fileStream, out directory);

            if (success)
            {
                if (!Verify.IsTrue(reader.Read(out byte numFlags))) return false;

                flags = new LocaleFlag[numFlags];
                for (int i = 0; i < numFlags; i++)
                {
                    if (!Verify.IsTrue(reader.Read(out ushort bitValue))) return false;
                    if (!Verify.IsTrue(reader.Read(out ushort bitMask))) return false;

                    if (ReadString(reader, filePath, fileStream, out string flagText))
                        flags[i] = new(bitValue, bitMask, flagText);
                }

            }

            return success;
        }

        private bool ReadString(CalligraphyReader reader, string filePath, Stream fileStream, out string value)
        {
            value = default;

            if (!Verify.IsTrue(reader.Read(out ushort stringLength), $"Failed to read string length in file {filePath} at position {fileStream.Position}"))
                return false;

            Span<byte> readBuffer = stackalloc byte[stringLength];

            if (!Verify.IsTrue(reader.ReadBytes(readBuffer), $"Failed to read string of length {stringLength} in file {filePath} at position {fileStream.Position}"))
                return false;

            value = Encoding.UTF8.GetString(readBuffer);
            return true;
        }
    }
}
