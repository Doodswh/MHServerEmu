using System.IO;
using System.Text;

namespace VerifyLogParser
{
    public enum LogParserResult
    {
        Success,
        GenericError,
        FileNotFound,
    }

    public static class LogParser
    {
        public static LogParserResult Parse(string logFilePath, Dictionary<uint, VerifyEntry> verifyEntries)
        {
            if (File.Exists(logFilePath) == false)
                return LogParserResult.FileNotFound;

            try
            {
                using StreamReader reader = new(logFilePath);

                Queue<string> logMessages = new();

                string line;
                while ((line = reader.ReadLine()) != null)
                    logMessages.Enqueue(line);

                while (logMessages.Count > 0)
                    ParseLogMessage(logMessages, verifyEntries);
            }
            catch (Exception)
            {
                return LogParserResult.GenericError;
            }

            return LogParserResult.Success;
        }

        private static void ParseLogMessage(Queue<string> logMessages, Dictionary<uint, VerifyEntry> verifyEntries)
        {
            string message = logMessages.Dequeue();

            if (message.Contains("[Verify] Verify failed:") == false)
                return;

            StringBuilder messageBuilder = new();
            messageBuilder.AppendLine(message);

            while (true)
            {
                if (logMessages.Count == 0)
                    return;

                string subLine = logMessages.Dequeue();
                bool isFileLine = TryParseFileLine(subLine, out string file, out int line, out string member);

                if (isFileLine)
                {
                    uint hash = HashFnv1a($"{file}:{line}");
                    if (verifyEntries.TryGetValue(hash, out VerifyEntry entry) == false)
                    {
                        Console.WriteLine($"{file}:{line} {member}");

                        string stackTrace = ParseStackTrace(logMessages);
                        entry = new(file, line, member, stackTrace);
                        verifyEntries.Add(hash, entry);
                    }

                    entry.Messages.Add(messageBuilder.ToString());
                    break;
                }
                else
                {
                    messageBuilder.AppendLine(subLine);
                }
            }
        }

        private static bool TryParseFileLine(string input, out string file, out int line, out string member)
        {
            const string FilePrefix = "File:";
            const string LinePrefix = "Line:";
            const string MemberPrefix = "Member:";

            file = default;
            line = default;
            member = default;

            int fileIndex = input.IndexOf(FilePrefix);
            if (fileIndex == -1)
                return false;

            int lineIndex = input.IndexOf(LinePrefix);
            if (lineIndex == -1)
                return false;

            int memberIndex = input.IndexOf(MemberPrefix);
            if (memberIndex == -1)
                return false;

            int fileStart = fileIndex + FilePrefix.Length;
            int fileLength = (lineIndex - 1) - fileStart;
            file = input.Substring(fileStart, fileLength);

            int lineStart = lineIndex + LinePrefix.Length;
            int lineLength = (memberIndex - 1) - lineStart;
            line = int.Parse(input.Substring(lineStart, lineLength));

            int memberStart = memberIndex + MemberPrefix.Length;
            int memberEnd = input.LastIndexOf(')') + 1 - memberStart;
            member = input.Substring(memberStart, memberEnd);

            return true;
        }

        private static string ParseStackTrace(Queue<string> logMessages)
        {
            StringBuilder stackTraceBuilder = new();

            while (true)
            {
                if (logMessages.Count == 0)
                    break;

                string stackTraceLine = logMessages.Peek();
                if (stackTraceLine.StartsWith("   at"))
                {
                    logMessages.Dequeue();
                    stackTraceBuilder.AppendLine(stackTraceLine);
                }
                else
                {
                    break;
                }
            }

            return stackTraceBuilder.ToString();
        }

        private static uint HashFnv1a(string input)
        {
            const uint FnvOffsetBasis = 0x811C9DC5;
            const uint FnvPrime = 0x1000193;

            uint hash = FnvOffsetBasis;

            int numBytes = Encoding.UTF8.GetByteCount(input);
            Span<byte> bytes = stackalloc byte[numBytes];
            Encoding.UTF8.GetBytes(input, bytes);

            foreach (byte b in bytes)
                hash = (hash ^ b) * FnvPrime;

            return hash;
        }
    }
}
