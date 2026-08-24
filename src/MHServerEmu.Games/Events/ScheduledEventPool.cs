using System.Text;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Memory;

namespace MHServerEmu.Games.Events
{
    /// <summary>
    /// Specialized pool for managing reusable object instances for <see cref="EventScheduler"/>.
    /// </summary>
    public class ScheduledEventPool
    {
        private static int _numRegisteredEventTypes = 0;

        private readonly EventListPool _eventListPool = new();

        private IObjectPool[] _eventPools;

        /// <summary>
        /// Constructs a new <see cref="ScheduledEventPool"/> instance.
        /// </summary>
        public ScheduledEventPool()
        {
            ResizeEventPoolArray();
        }

        /// <summary>
        /// Retrieves or creates a new <see cref="ScheduledEvent"/> instance of subtype <typeparamref name="T"/>.
        /// </summary>
        public T Get<T>() where T: ScheduledEvent, new()
        {
            ushort eventTypeId = EventPool<T>.EventTypeId;
            if (eventTypeId >= _eventPools.Length)
                ResizeEventPoolArray();

            ref IObjectPool eventPool = ref _eventPools[eventTypeId];
            eventPool ??= new EventPool<T>();

            return ((EventPool<T>)eventPool).Get();
        }

        /// <summary>
        /// Returns a <see cref="ScheduledEvent"/> instance to the pool.
        /// </summary>
        public void Return(ScheduledEvent @event)
        {
            ushort eventTypeId = @event.EventTypeId;
            if (!Verify.IsTrue(eventTypeId < _eventPools.Length, LoggingLevel.Error)) return;

            IObjectPool eventPool = _eventPools[eventTypeId];
            if (!Verify.IsNotNull(eventPool, LoggingLevel.Error)) return;

            eventPool.ReturnUnsafe(@event);
        }

        /// <summary>
        /// Retrieves or creates a new <see cref="LinkedList{T}"/> instance.
        /// </summary>
        public LinkedList<ScheduledEvent> GetList()
        {
            return _eventListPool.Get();
        }

        /// <summary>
        /// Returns a <see cref="LinkedList{T}"/> instance to the pool.
        /// </summary>
        public void ReturnList(LinkedList<ScheduledEvent> eventList)
        {
            _eventListPool.Return(eventList);
        }

        /// <summary>
        /// Returns a <see cref="string"/> representing the current state of this <see cref="ScheduledEventPool"/> instance.
        /// </summary>
        public string GetReportString()
        {
            StringBuilder sb = new();

            sb.AppendLine("Name\tActive\tInactive\tTotal");

            // Accuracy > efficiency here, so recalculate all counts using the data from actual subpools
            int activeSum = 0;
            int inactiveSum = 0;
            int totalSum = 0;

            foreach (IObjectPool eventPool in _eventPools)
            {
                if (eventPool == null)
                    continue;

                string name = eventPool.GetType().GenericTypeArguments[0].Name;
                int active = eventPool.CountActive;
                int inactive = eventPool.CountInactive;
                int total = eventPool.CountTotal;

                activeSum += active;
                inactiveSum += inactive;
                totalSum += total;

                sb.AppendLine($"{name}\t{active}\t{inactive}\t{total}");
            }

            sb.AppendLine();
            sb.AppendLine($"TOTAL\t{activeSum}\t{inactiveSum}\t{totalSum}");

            sb.AppendLine($"EventListPoolCount\t{_eventListPool.CountActive}\t{_eventListPool.CountInactive}\t{_eventListPool.CountTotal}");

            return sb.ToString();
        }

        private void ResizeEventPoolArray()
        {
            const int InitialSize = 4;

            int size = InitialSize;
            while (size < _numRegisteredEventTypes)
                size *= 2;

            Verify.IsTrue(size <= ushort.MaxValue, LoggingLevel.Error);

            if (_eventPools == null)
                _eventPools = new IObjectPool[size];
            else if (_eventPools.Length < size)
                Array.Resize(ref _eventPools, size);
        }

        private static ushort RegisterEventType()
        {
            int eventTypeId = Interlocked.Increment(ref _numRegisteredEventTypes) - 1;
            Verify.IsTrue(eventTypeId <= ushort.MaxValue, LoggingLevel.Error);
            return (ushort)eventTypeId;
        }

        private sealed class EventPool<T> : ObjectPool<T> where T : ScheduledEvent, new()
        {
            public static readonly ushort EventTypeId = RegisterEventType();

            public EventPool() : base(ObjectPoolFlags.None) { }

            protected override T Allocate()
            {
                return new() { EventTypeId = EventTypeId };
            }

            protected override void OnGet(T instance)
            {
                if (CountTotal > 16384)
                    Verify.IsTrue(false, $"OnGet(): {instance}");
            }

            protected override int GetAllocationWarningThreshold()
            {
                return 16384;
            }
        }

        private sealed class EventListPool : ObjectPool<LinkedList<ScheduledEvent>>
        {
            public EventListPool() : base(ObjectPoolFlags.None) { }

            protected override LinkedList<ScheduledEvent> Allocate()
            {
                return new();
            }

            protected override int GetAllocationWarningThreshold()
            {
                return 32768;
            }
        }
    }
}
