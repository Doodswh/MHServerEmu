using MHServerEmu.Games;
using MHServerEmu.Games.Scripting;

namespace MHServerEmu.Scripting.CSharp
{
    /// <summary>
    /// Globals available to .csx scripts: Hooks, Log, ScriptName, IsLoaded and After(...).
    /// </summary>
    public sealed class ScriptGlobals
    {
        private volatile bool _isLoaded = true;

        public ScriptHookApi Hooks { get; }
        public ScriptLog Log { get; }
        public string ScriptName { get; }

        /// <summary>
        /// <see langword="false"/> once this script has been unloaded or replaced by a hot reload.
        /// </summary>
        public bool IsLoaded { get => _isLoaded; }

        public ScriptGlobals(string scriptName)
        {
            ScriptName = scriptName;
            Hooks = new(scriptName);
            Log = new(scriptName);
        }

        /// <summary>
        /// Runs <paramref name="callback"/> on <paramref name="game"/>'s thread after <paramref name="seconds"/>.
        /// Call from that game's thread (inside a hook handler or another After callback).
        /// Callbacks are skipped once this script is unloaded or reloaded.
        /// </summary>
        public bool After(Game game, float seconds, Action callback)
        {
            return ScriptTimer.After(game, seconds, callback, () => _isLoaded);
        }

        internal void MarkUnloaded()
        {
            _isLoaded = false;
        }
    }

    /// <summary>
    /// Registers handlers on behalf of a .csx script so they are removed when the script is unloaded.
    /// Usage: Hooks.On(ScriptHooks.EntityKilled, e => { ... });
    /// </summary>
    public sealed class ScriptHookApi
    {
        private readonly string _owner;

        public ScriptHookApi(string owner)
        {
            _owner = owner;
        }

        public void On<TArgs>(ScriptHook<TArgs> hook, Action<TArgs> handler, int priority = 0) where TArgs : class
        {
            hook.Register(_owner, handler, priority);
        }
    }
}
