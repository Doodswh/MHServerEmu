namespace VerifyLogParser
{
    public class VerifyEntry
    {
        public string File { get; }
        public int Line { get; }
        public string Member { get; }
        public string StackTrace { get; }

        public List<string> Messages { get; } = new();

        public VerifyEntry(string file, int line, string member, string stackTrace)
        {
            File = file;
            Line = line;
            Member = member;
            StackTrace = stackTrace;
        }

        public override string ToString()
        {
            return $"{File}:{Line}";
        }
    }
}
