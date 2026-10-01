using MHServerEmu.Core.Logging;
using MHServerEmu.Games.Scripting;
using MoonSharp.Interpreter;

namespace MHServerEmu.Scripting.Lua
{
    /// <summary>
    /// Runs a single .lua file with MoonSharp. Lua API:
    ///   hooks.on(name, function(e) ... end [, priority])   -- attach a handler to a hook from ScriptHooks
    ///   hooks.list()                                        -- names of all available hooks
    ///   log.info(msg) / log.warn(msg) / log.error(msg)
    ///   print(...)                                          -- same as log.info
    ///   ui.register(key, text)                              -- register on-screen text (do this at load time)
    ///   ui.registerRange(key, "Wave {0}!", from, to)        -- one string per number, {0} is replaced
    ///   ui.isRegistered(key, number)
    /// </summary>
    /// <remarks>
    /// MoonSharp is not thread-safe and hooks are invoked from multiple game threads, so every call into this script is serialized.
    /// Scripts run in a soft sandbox (no io / os / load / require).
    /// </remarks>
    internal sealed class LuaScriptHost : IScriptHost
    {
        private static readonly Logger Logger = LogManager.CreateLogger();
        private static bool _typesRegistered;
        private static readonly object _typeRegistrationLock = new();

        private readonly object _lock = new();
        private Script _script;

        public string Owner { get; }

        public LuaScriptHost(string owner)
        {
            Owner = owner;
        }

        public bool Load(string code)
        {
            RegisterHookArgTypes();

            ScriptLog log = new(Owner);

            Script script = new(CoreModules.Preset_SoftSandbox);
            script.Options.DebugPrint = text => log.Info(text);

            Table hooks = new(script);
            hooks["on"] = DynValue.NewCallback(On);
            hooks["list"] = DynValue.NewCallback((context, args) =>
                DynValue.FromObject(script, ScriptHooks.All.Select(hook => hook.Name).ToList()));
            script.Globals["hooks"] = hooks;

            Table logTable = new(script);
            logTable["info"] = (Action<string>)log.Info;
            logTable["warn"] = (Action<string>)log.Warn;
            logTable["error"] = (Action<string>)log.Error;
            script.Globals["log"] = logTable;

            // On-screen text: register at load time, show with e:ShowBanner(key [, number [, style [, ms]]]) on hook args
            Table uiTable = new(script);
            uiTable["register"] = (Func<string, string, bool>)ScriptText.Register;
            uiTable["registerRange"] = (Func<string, string, int, int, bool>)ScriptText.RegisterRange;
            uiTable["isRegistered"] = (Func<string, int, bool>)ScriptText.IsRegistered;
            script.Globals["ui"] = uiTable;

            _script = script;

            try
            {
                lock (_lock)
                    script.DoString(code, null, Owner);
            }
            catch (InterpreterException e)
            {
                Logger.Error($"Load(): Failed to run [{Owner}]: {e.DecoratedMessage ?? e.Message}");

                // Drop anything the script registered before it failed
                ScriptHooks.UnregisterOwner(Owner);
                _script = null;
                return false;
            }

            return true;
        }

        public void Dispose()
        {
            ScriptHooks.UnregisterOwner(Owner);
            _script = null;
        }

        private DynValue On(ScriptExecutionContext context, CallbackArguments args)
        {
            string hookName = args.AsType(0, "hooks.on", DataType.String).String;
            DynValue function = args.AsType(1, "hooks.on", DataType.Function);
            int priority = args.Count > 2 && args[2].Type == DataType.Number ? (int)args[2].Number : 0;

            if (ScriptHooks.TryGetHook(hookName, out IScriptHook hook) == false)
                throw new ScriptRuntimeException($"hooks.on: unknown hook '{hookName}'");

            hook.RegisterUntyped(Owner, hookArgs => CallHandler(hook.Name, function, hookArgs), priority);
            return DynValue.Nil;
        }

        private void CallHandler(string hookName, DynValue function, object hookArgs)
        {
            lock (_lock)
            {
                Script script = _script;
                if (script == null)
                    return;

                try
                {
                    script.Call(function, UserData.Create(hookArgs));
                }
                catch (InterpreterException e)
                {
                    Logger.Error($"CallHandler(): [{Owner}] handler for [{hookName}] failed: {e.DecoratedMessage ?? e.Message}");
                }
            }
        }

        private static void RegisterHookArgTypes()
        {
            if (_typesRegistered)
                return;

            lock (_typeRegistrationLock)
            {
                if (_typesRegistered)
                    return;

                // Only hook args are exposed to Lua. Their simple value properties (names, ids, flags) and helper methods
                // are usable directly; raw game objects (Game, entities, prototypes) are not registered and stay C#-only.
                foreach (IScriptHook hook in ScriptHooks.All)
                    UserData.RegisterType(hook.ArgsType);

                _typesRegistered = true;
            }
        }
    }
}
