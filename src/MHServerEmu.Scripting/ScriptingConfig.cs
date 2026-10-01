using MHServerEmu.Core.Config;

namespace MHServerEmu.Scripting
{
    public class ScriptingConfig : ConfigContainer
    {
        public bool EnableScripting { get; private set; } = true;
        public string ScriptDirectory { get; private set; } = "Data/Scripts";
        public bool EnableLua { get; private set; } = true;
        public bool EnableCSharp { get; private set; } = true;
        public bool HotReload { get; private set; } = true;
    }
}
