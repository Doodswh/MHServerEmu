namespace MHServerEmu.Scripting
{
    /// <summary>
    /// A loaded script file. Handlers registered by the script are owned by <see cref="Owner"/>
    /// and removed from the hook registry when the script is unloaded.
    /// </summary>
    internal interface IScriptHost : IDisposable
    {
        public string Owner { get; }
        public bool Load(string code);
    }
}
