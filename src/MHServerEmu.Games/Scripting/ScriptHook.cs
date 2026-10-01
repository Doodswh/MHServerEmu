using MHServerEmu.Core.Logging;

namespace MHServerEmu.Games.Scripting
{
    /// <summary>
    /// Non-generic view of a <see cref="ScriptHook{TArgs}"/> used by script bridges that look hooks up by name.
    /// </summary>
    public interface IScriptHook
    {
        public string Name { get; }
        public Type ArgsType { get; }
        public int HandlerCount { get; }

        /// <summary>
        /// Registers a handler that receives the hook args as <see cref="object"/>.
        /// </summary>
        public void RegisterUntyped(string owner, Action<object> handler, int priority = 0);

        /// <summary>
        /// Removes all handlers registered by the specified owner. Returns the number of removed handlers.
        /// </summary>
        public int UnregisterOwner(string owner);
    }

    /// <summary>
    /// A named extension point in the game simulation that scripts can attach handlers to.
    /// </summary>
    /// <remarks>
    /// Invoke is called from game threads, so handler lists are copy-on-write arrays: invoking never takes a lock,
    /// and checking <see cref="HasHandlers"/> is a single array length read when nothing is registered.
    /// Handlers run in ascending priority order. Exceptions thrown by a handler are logged and do not stop other handlers.
    /// </remarks>
    public sealed class ScriptHook<TArgs> : IScriptHook where TArgs : class
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private readonly struct Entry
        {
            public readonly string Owner;
            public readonly int Priority;
            public readonly Action<TArgs> Handler;

            public Entry(string owner, int priority, Action<TArgs> handler)
            {
                Owner = owner;
                Priority = priority;
                Handler = handler;
            }
        }

        private readonly object _writeLock = new();
        private volatile Entry[] _entries = Array.Empty<Entry>();

        public string Name { get; }
        public Type ArgsType { get => typeof(TArgs); }
        public int HandlerCount { get => _entries.Length; }
        public bool HasHandlers { get => _entries.Length > 0; }

        public ScriptHook(string name)
        {
            Name = name;
        }

        public override string ToString() => $"{Name} ({HandlerCount} handlers)";

        public void Register(string owner, Action<TArgs> handler, int priority = 0)
        {
            ArgumentNullException.ThrowIfNull(owner);
            ArgumentNullException.ThrowIfNull(handler);

            lock (_writeLock)
            {
                Entry[] current = _entries;
                List<Entry> updated = new(current.Length + 1);
                updated.AddRange(current);
                updated.Add(new(owner, priority, handler));

                // Stable sort by priority so handlers with the same priority keep registration order
                _entries = updated.OrderBy(entry => entry.Priority).ToArray();
            }
        }

        public void RegisterUntyped(string owner, Action<object> handler, int priority = 0)
        {
            ArgumentNullException.ThrowIfNull(handler);
            Register(owner, args => handler(args), priority);
        }

        public int UnregisterOwner(string owner)
        {
            lock (_writeLock)
            {
                Entry[] current = _entries;
                Entry[] updated = current.Where(entry => entry.Owner != owner).ToArray();
                _entries = updated;
                return current.Length - updated.Length;
            }
        }

        public void Invoke(TArgs args)
        {
            Entry[] entries = _entries;

            for (int i = 0; i < entries.Length; i++)
            {
                try
                {
                    entries[i].Handler(args);
                }
                catch (Exception e)
                {
                    Logger.ErrorException(e, $"Invoke(): Handler from [{entries[i].Owner}] for hook [{Name}] threw an exception");
                }
            }
        }
    }
}
