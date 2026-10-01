using System.Collections.Concurrent;
using MHServerEmu.Core.Config;
using MHServerEmu.Core.Helpers;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Network;
using MHServerEmu.Games.Achievements;
using MHServerEmu.Games.Scripting;
using MHServerEmu.Scripting.CSharp;
using MHServerEmu.Scripting.Lua;

namespace MHServerEmu.Scripting
{
    /// <summary>
    /// Script bridge service. Loads .lua (MoonSharp) and .csx (Roslyn) files from the script directory and
    /// attaches their handlers to the shared <see cref="ScriptHooks"/> registry. The game simulation never references this.
    /// </summary>
    /// <remarks>
    /// Files are loaded recursively. Files ending in .example (or any other extension) are ignored, as are
    /// files and folders whose name starts with an underscore. With HotReload enabled, saving a script reloads it.
    /// </remarks>
    public class ScriptManager : IGameService
    {
        private const int ReloadDebounceMS = 500;

        private static readonly Logger Logger = LogManager.CreateLogger();

        private readonly ConcurrentDictionary<string, IScriptHost> _scripts = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Timer> _pendingReloads = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _loadLock = new();

        private ScriptingConfig _config;
        private string _scriptDirectory;
        private FileSystemWatcher _watcher;

        public GameServiceState State { get; private set; } = GameServiceState.Created;

        public void Run()
        {
            _config = ConfigManager.Instance.GetConfig<ScriptingConfig>();

            if (_config.EnableScripting == false)
            {
                Logger.Info("Scripting is disabled");
                State = GameServiceState.Running;
                return;
            }

            _scriptDirectory = Path.GetFullPath(Path.Combine(FileHelper.ServerRoot, _config.ScriptDirectory));
            if (Directory.Exists(_scriptDirectory) == false)
                Directory.CreateDirectory(_scriptDirectory);

            LoadAll();

            if (_config.HotReload)
                StartWatcher();

            State = GameServiceState.Running;
        }

        public void Shutdown()
        {
            _watcher?.Dispose();
            _watcher = null;

            foreach (Timer timer in _pendingReloads.Values)
                timer.Dispose();
            _pendingReloads.Clear();

            lock (_loadLock)
            {
                foreach (IScriptHost host in _scripts.Values)
                    host.Dispose();
                _scripts.Clear();
            }

            State = GameServiceState.Shutdown;
        }

        public void ReceiveServiceMessage<T>(in T message) where T : struct, IGameServiceMessage
        {
        }

        public void GetStatus(Dictionary<string, long> statusDict)
        {
            statusDict["ScriptsLoaded"] = _scripts.Count;

            foreach (IScriptHook hook in ScriptHooks.All)
                statusDict[$"ScriptHook_{hook.Name}"] = hook.HandlerCount;
        }

        /// <summary>
        /// Unloads everything and loads all scripts from the script directory again.
        /// </summary>
        public void ReloadAll()
        {
            lock (_loadLock)
            {
                foreach (IScriptHost host in _scripts.Values)
                    host.Dispose();
                _scripts.Clear();
            }

            LoadAll();
        }

        private void LoadAll()
        {
            int loaded = 0;
            int failed = 0;

            foreach (string filePath in Directory.EnumerateFiles(_scriptDirectory, "*", SearchOption.AllDirectories).OrderBy(path => path))
            {
                if (IsScriptFile(filePath) == false)
                    continue;

                if (LoadFile(filePath))
                    loaded++;
                else
                    failed++;
            }

            Logger.Info($"Loaded {loaded} script(s) from {_scriptDirectory}" + (failed > 0 ? $", {failed} failed" : string.Empty));

            // Omega item generation lives entirely in a script (omega/omega_items.csx), so make its absence visible
            if (ScriptHooks.ItemAffixesRolling.HasHandlers == false)
                Logger.Warn("No script handles ItemAffixesRolling: Omega difficulty drops will be normal items (see Data/Scripts/omega/omega_items.csx)");

            RebuildTextDump();
        }

        /// <summary>
        /// Rebuilds the achievement dump (which carries script on-screen text) here instead of on the game thread of the
        /// next player to connect. Does nothing if no text changed.
        /// </summary>
        private static void RebuildTextDump()
        {
            int customStrings = AchievementDatabase.Instance.CustomStringCount;
            if (customStrings == 0)
                return;

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            AchievementDatabase.Instance.GetDump();
            Logger.Info($"Script on-screen text ready ({customStrings} strings, {stopwatch.ElapsedMilliseconds} ms)");
        }

        private bool LoadFile(string filePath)
        {
            string owner = Path.GetRelativePath(_scriptDirectory, filePath);

            lock (_loadLock)
            {
                if (_scripts.TryRemove(owner, out IScriptHost existing))
                    existing.Dispose();

                if (File.Exists(filePath) == false)
                    return false;

                IScriptHost host = CreateHost(filePath, owner);
                if (host == null)
                    return false;

                string code;
                try
                {
                    code = File.ReadAllText(filePath);
                }
                catch (IOException e)
                {
                    Logger.Warn($"LoadFile(): Failed to read [{owner}]: {e.Message}");
                    return false;
                }

                if (host.Load(code) == false)
                    return false;

                _scripts[owner] = host;
                Logger.Info($"Loaded script [{owner}]");
                return true;
            }
        }

        private void UnloadFile(string filePath)
        {
            string owner = Path.GetRelativePath(_scriptDirectory, filePath);

            lock (_loadLock)
            {
                if (_scripts.TryRemove(owner, out IScriptHost existing))
                {
                    existing.Dispose();
                    Logger.Info($"Unloaded script [{owner}]");
                }
            }
        }

        private IScriptHost CreateHost(string filePath, string owner)
        {
            string extension = Path.GetExtension(filePath);

            if (extension.Equals(".lua", StringComparison.OrdinalIgnoreCase))
                return _config.EnableLua ? new LuaScriptHost(owner) : null;

            if (extension.Equals(".csx", StringComparison.OrdinalIgnoreCase))
                return _config.EnableCSharp ? new CSharpScriptHost(owner) : null;

            return null;
        }

        private bool IsScriptFile(string filePath)
        {
            string extension = Path.GetExtension(filePath);
            if (extension.Equals(".lua", StringComparison.OrdinalIgnoreCase) == false && extension.Equals(".csx", StringComparison.OrdinalIgnoreCase) == false)
                return false;

            // Skip files and folders starting with an underscore
            string relativePath = Path.GetRelativePath(_scriptDirectory, filePath);
            foreach (string part in relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                if (part.StartsWith('_'))
                    return false;
            }

            return true;
        }

        #region Hot Reload

        private void StartWatcher()
        {
            _watcher = new(_scriptDirectory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };

            _watcher.Changed += (sender, e) => QueueReload(e.FullPath);
            _watcher.Created += (sender, e) => QueueReload(e.FullPath);
            _watcher.Deleted += (sender, e) => QueueReload(e.FullPath);
            _watcher.Renamed += (sender, e) =>
            {
                QueueReload(e.OldFullPath);
                QueueReload(e.FullPath);
            };

            _watcher.EnableRaisingEvents = true;
            Logger.Info("Script hot reload enabled");
        }

        private void QueueReload(string filePath)
        {
            string extension = Path.GetExtension(filePath);
            if (extension.Equals(".lua", StringComparison.OrdinalIgnoreCase) == false && extension.Equals(".csx", StringComparison.OrdinalIgnoreCase) == false)
                return;

            // Editors fire several events per save, so wait for them to settle before reloading
            Timer timer = _pendingReloads.GetOrAdd(filePath, path => new Timer(_ => ProcessReload(path), null, Timeout.Infinite, Timeout.Infinite));
            timer.Change(ReloadDebounceMS, Timeout.Infinite);
        }

        private void ProcessReload(string filePath)
        {
            if (_pendingReloads.TryRemove(filePath, out Timer timer))
                timer.Dispose();

            try
            {
                if (File.Exists(filePath) && IsScriptFile(filePath))
                    LoadFile(filePath);
                else
                    UnloadFile(filePath);

                RebuildTextDump();
            }
            catch (Exception e)
            {
                Logger.ErrorException(e, $"ProcessReload(): Failed to reload [{filePath}]");
            }
        }

        #endregion
    }
}
