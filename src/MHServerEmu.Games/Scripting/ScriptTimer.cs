using MHServerEmu.Core.Logging;
using MHServerEmu.Games.Events;

namespace MHServerEmu.Games.Scripting
{
    /// <summary>
    /// Runs script callbacks later through a game's own event scheduler, so they run on that game's thread and can
    /// safely touch its regions and entities.
    /// </summary>
    /// <remarks>
    /// Schedule only from that game's thread (hook handlers and other timer callbacks run there).
    /// Pass <c>isAlive</c> so callbacks from a script that has since been unloaded or reloaded do nothing.
    /// </remarks>
    public static class ScriptTimer
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        public const float MaxDelaySeconds = 86400f;

        /// <summary>
        /// Runs <paramref name="callback"/> on <paramref name="game"/>'s thread after <paramref name="seconds"/>.
        /// </summary>
        public static bool After(Game game, float seconds, Action callback, Func<bool> isAlive = null)
        {
            if (game == null || callback == null)
                return false;

            EventScheduler scheduler = game.GameEventScheduler;
            if (scheduler == null)
                return false;

            EventPointer<ScriptCallbackEvent> pointer = new();
            if (scheduler.ScheduleEvent(pointer, TimeSpan.FromSeconds(Math.Clamp(seconds, 0f, MaxDelaySeconds))) == false)
                return false;

            pointer.Get().Initialize(callback, isAlive);
            return true;
        }

        private sealed class ScriptCallbackEvent : ScheduledEvent
        {
            private Action _callback;
            private Func<bool> _isAlive;

            public void Initialize(Action callback, Func<bool> isAlive)
            {
                _callback = callback;
                _isAlive = isAlive;
            }

            public override bool OnTriggered()
            {
                if (_callback == null || (_isAlive != null && _isAlive() == false))
                    return true;

                try
                {
                    _callback();
                }
                catch (Exception e)
                {
                    Logger.ErrorException(e, "OnTriggered(): Script timer callback threw an exception");
                }

                return true;
            }

            public override void Clear()
            {
                _callback = null;
                _isAlive = null;
            }
        }
    }
}
