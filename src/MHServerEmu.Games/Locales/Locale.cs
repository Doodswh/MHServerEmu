using System.Text;
using MHServerEmu.Core.Extensions;
using MHServerEmu.Core.Helpers;
using MHServerEmu.Core.Logging;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Calligraphy;

namespace MHServerEmu.Games.Locales
{
    public enum LocaleLanguage
    {
        Chinese,
        English,
        French,
        German,
        GreekCypher,
        Japanese,
        Korean,
        PigLatin,
        Placeholder,
        Portuguese,
        Russian,
        Spanish,

        NumLanguages,
        Invalid,
    }

    public enum LocaleRegion
    {
        All,

        NumRegions,
        Invalid,
    }

    public class Locale
    {
        private readonly LocaleManager _localeManager;

        private readonly List<LocaleFlag> _localeFlags = new();
        private readonly Dictionary<LocaleStringId, LocaleDefaultString> _stringMap = new();

        public string StringFileDirectory { get; }
        public string Name { get; }
        public LocaleLanguage Language { get; }
        public string LanguageDisplayName { get; }
        public LocaleRegion Region { get; }
        public string RegionDisplayName { get; }
        public string Directory { get; }

        public Locale(LocaleManager localeManager, string filePath, string name, LocaleLanguage language, string languageDisplayName,
            LocaleRegion region, string regionDisplayName, string directory)
        {
            _localeManager = localeManager;

            Name = name;
            Language = language;
            LanguageDisplayName = languageDisplayName;
            Region = region;
            RegionDisplayName = regionDisplayName;
            Directory = directory;

            StringFileDirectory = Path.Combine(Path.Combine(Path.GetDirectoryName(filePath), directory));
        }

        public override string ToString()
        {
            return Name;
        }

        public bool Initialize(LocaleFlag[] flags)
        {
            if (!Verify.IsNotNull(_localeManager)) return false;
            if (!Verify.IsTrue(System.IO.Directory.Exists(StringFileDirectory))) return false;
            if (!Verify.IsTrue(string.IsNullOrEmpty(Name) == false)) return false;
            if (!Verify.IsTrue(Language != LocaleLanguage.Invalid)) return false;
            if (!Verify.IsTrue(string.IsNullOrEmpty(LanguageDisplayName) == false)) return false;
            if (!Verify.IsTrue(Region != LocaleRegion.Invalid)) return false;
            if (!Verify.IsTrue(string.IsNullOrEmpty(RegionDisplayName) == false)) return false;
            if (!Verify.IsTrue(string.IsNullOrEmpty(Directory) == false)) return false;

            _localeFlags.AddRange(flags);

            // Gazillion::TagResolver::setLocale()
            return true;
        }

        public void OnSetCurrent()
        {
            LoadStringFilesInDirectory(StringFileDirectory);
            // loadFormatStrings();
        }

        public void OnUnsetCurrent()
        {
            // removeFormatStrings();

            _stringMap.Clear();
        }

        public bool ImportStringStream(string streamName, Stream stream)
        {
            using CalligraphyReader reader = new(stream, streamName);

            if (!Verify.IsTrue(reader.ReadHeader("STR", DataDirectory.CalligraphyStringVersion))) return false;

            if (!Verify.IsTrue(reader.Read(out ushort nNumStrings))) return false;

            if (nNumStrings == 0)
                return Verify.IsTrue(reader.BytesRemaining == 0);

            // Allocate and load a StringPage for this stream.
            // Because C# doesn't use C strings natively, we can't make use of StringPage instances directly,
            // so we discard them after converting everything to native C# strings.
            
            // Mark where entries start
            long startOffset = reader.Seek(SeekOrigin.Current, 0);

            // Read through the first entry to figure out where the string page starts.
            if (!Verify.IsTrue(reader.Read(out LocaleStringId localeStringId))) return false;
            if (!Verify.IsTrue(reader.Read(out ushort numVariants))) return false;
            if (!Verify.IsTrue(reader.Read(out ushort flagsProduced))) return false;
            if (!Verify.IsTrue(reader.Read(out uint offset))) return false;

            // Calculate string page size and move to it. 
            uint stringPageOffset = offset;
            long stringPageSize = reader.Seek(SeekOrigin.End, 0) - stringPageOffset;
            reader.Seek(SeekOrigin.Begin, offset);
            
            // Allocate the string page
            StringPage page = new(new byte[stringPageSize], HashHelper.Djb2(streamName));
            if (!Verify.IsNotNull(page.Buffer)) return false;

            // Load it and move back to the first entry
            if (!Verify.IsTrue(reader.ReadBytes(page.Buffer))) return false;

            reader.Seek(SeekOrigin.Begin, startOffset);

            // Read entries
            for (int i = 0; i < nNumStrings; i++)
            {
                if (!Verify.IsTrue(reader.Read(out localeStringId))) return false;
                if (!Verify.IsTrue(reader.Read(out numVariants))) return false;
                if (!Verify.IsTrue(reader.Read(out flagsProduced))) return false;
                if (!Verify.IsTrue(reader.Read(out offset))) return false;

                string text = page.GetCString((int)(offset - stringPageOffset));

                Verify.IsTrue(_stringMap.Remove(localeStringId, out LocaleDefaultString entry) == false,
                    $"Duplicate string id {(ulong)localeStringId} found in string map.  Existing string = '{entry.String}', new string = '{text}'");

                entry = new(localeStringId, flagsProduced, text);
                _stringMap.Add(localeStringId, entry);

                for (int j = 1; j < numVariants; j++)
                {
                    if (!Verify.IsTrue(reader.Read(out ulong flagsConsumed))) return false;
                    if (!Verify.IsTrue(reader.Read(out flagsProduced))) return false;
                    if (!Verify.IsTrue(reader.Read(out offset))) return false;

                    text = page.GetCString((int)(offset - stringPageOffset));

                    entry.Insert(flagsConsumed, flagsProduced, text);
                }
            }

            return true;
        }

        public string GetLocaleString(LocaleStringId stringId)
        {
            if (_stringMap.TryGetValue(stringId, out LocaleDefaultString entry) == false)
                return string.Empty;

            return entry.String;
        }

        private void LoadStringFilesInDirectory(string directory)
        {
            if (System.IO.Directory.Exists(directory) == false)
                return;

            if (!Verify.IsNotNull(_localeManager)) return;

            foreach (string filePath in System.IO.Directory.GetFiles(directory, "*.string"))
                LoadStringFile(filePath);
        }

        private void LoadStringFile(string filePath)
        {
            using FileStream fileStream = File.OpenRead(filePath);

            if (ImportStringStream(filePath, fileStream) == false)
                return;
        }

        private readonly struct StringPage(byte[] buffer, uint hash)
        {
            public readonly byte[] Buffer = buffer;
            public readonly uint Hash = hash;

            public string GetCString(int start)
            {
                int length = -1;

                for (int i = start; i < Buffer.Length; i++)
                {
                    if (Buffer[i] == 0)
                    {
                        length = i - start;
                        break;
                    }
                }

                return Encoding.UTF8.GetString(Buffer.AsSpan(start, length));
            }
        }
    }

    public readonly struct LocaleFlag(ushort bitValue, ushort bitMask, string flagText)
    {
        public readonly ushort BitValue = bitValue;
        public readonly ushort BitMask = bitMask;
        public readonly string FlagText = flagText;

        public override string ToString()
        {
            return FlagText;
        }
    }
}
