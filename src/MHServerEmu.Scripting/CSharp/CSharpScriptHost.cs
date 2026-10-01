using MHServerEmu.Core.Logging;
using MHServerEmu.Games;
using MHServerEmu.Games.Scripting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace MHServerEmu.Scripting.CSharp
{
    /// <summary>
    /// Compiles and runs a single .csx file with Roslyn. Scripts get <see cref="ScriptGlobals"/> as globals.
    /// </summary>
    /// <remarks>
    /// C# scripts are trusted code with full access to the game assemblies. Handlers run on game threads,
    /// so scripts must not block or touch objects that belong to a different game.
    /// Compiled script assemblies cannot be unloaded, so every hot reload of a .csx file keeps a little memory until restart.
    /// </remarks>
    internal sealed class CSharpScriptHost : IScriptHost
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private static readonly ScriptOptions Options = ScriptOptions.Default
            .WithReferences(
                typeof(Game).Assembly,                  // MHServerEmu.Games
                typeof(Logger).Assembly,                // MHServerEmu.Core
                typeof(ScriptGlobals).Assembly)         // MHServerEmu.Scripting
            .WithImports(
                "System",
                "System.Linq",
                "System.Collections.Generic",
                "MHServerEmu.Core.VectorMath",
                "MHServerEmu.Games",
                "MHServerEmu.Games.Entities",
                "MHServerEmu.Games.Entities.Avatars",
                "MHServerEmu.Games.GameData",
                "MHServerEmu.Games.GameData.Prototypes",
                "MHServerEmu.Games.Properties",
                "MHServerEmu.Games.Regions",
                "MHServerEmu.Games.Scripting")
            .WithOptimizationLevel(OptimizationLevel.Release);

        private ScriptGlobals _globals;

        public string Owner { get; }

        public CSharpScriptHost(string owner)
        {
            Owner = owner;
        }

        public bool Load(string code)
        {
            _globals = new(Owner);

            try
            {
                CSharpScript.RunAsync(code, Options, _globals, typeof(ScriptGlobals)).GetAwaiter().GetResult();
                return true;
            }
            catch (CompilationErrorException e)
            {
                Logger.Error($"Load(): Failed to compile [{Owner}]:\n{string.Join("\n", e.Diagnostics)}");
            }
            catch (Exception e)
            {
                Logger.ErrorException(e, $"Load(): Failed to run [{Owner}]");
            }

            // Drop anything the script registered before it failed
            Dispose();
            return false;
        }

        public void Dispose()
        {
            _globals?.MarkUnloaded();
            ScriptHooks.UnregisterOwner(Owner);
        }
    }
}
