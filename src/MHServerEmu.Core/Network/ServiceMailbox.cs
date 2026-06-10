using MHServerEmu.Core.Collections;
using MHServerEmu.Core.Memory;

namespace MHServerEmu.Core.Network
{
    /// <summary>
    /// Base class for unboxed <see cref="IGameServiceMessage"/> envelopes.
    /// </summary>
    public abstract class MessageEnvelope
    {
        public abstract void Dispatch(ServiceMailbox mailbox);
        public abstract void Release();
    }

    /// <summary>
    /// Generic envelope that holds the struct as a raw field, preventing boxing.
    /// </summary>
    public class MessageEnvelope<T> : MessageEnvelope where T : struct, IGameServiceMessage
    {
        // Thread-safe, cross-thread pool specifically for this message struct type
        private static readonly ConcurrentPool<MessageEnvelope<T>> Pool = new(4096, static () => new MessageEnvelope<T>());

        public T Payload;

        public override void Dispatch(ServiceMailbox mailbox)
        {
            // Passes the unboxed struct back to the mailbox handler
            mailbox.HandleMessage(in Payload);
        }

        public override void Release()
        {
            // Clear references to allow GC to clean up payloads like IFrontendClient
            Payload = default;

            // Return to the thread-safe global pool
            Pool.Return(this);
        }

        // Helper to grab from the pool
        public static MessageEnvelope<T> GetFromPool(in T message)
        {
            var envelope = Pool.Get();
            envelope.Payload = message;
            return envelope;
        }
    }

    /// <summary>
    /// Base class for <see cref="IGameServiceMessage"/> handlers.
    /// </summary>
    public abstract class ServiceMailbox
    {
        // The queue now holds the unboxed class wrapper instead of the interface
        private readonly DoubleBufferQueue<MessageEnvelope> _messageQueue = new();

        /// <summary>
        /// Called from other threads to post an <see cref="IGameServiceMessage"/>
        /// </summary>
        public void PostMessage<T>(in T message) where T : struct, IGameServiceMessage
        {
            // Grab a zero-allocation wrapper from the concurrent pool and assign the struct
            var envelope = MessageEnvelope<T>.GetFromPool(in message);
            _messageQueue.Enqueue(envelope);
        }

        /// <summary>
        /// Processes enqueued <see cref="IGameServiceMessage"/> instances.
        /// </summary>
        public void ProcessMessages()
        {
            _messageQueue.Swap();

            while (_messageQueue.CurrentCount > 0)
            {
                MessageEnvelope envelope = _messageQueue.Dequeue();

                // Dispatch the message and release it back to the cross-thread pool
                envelope.Dispatch(this);
                envelope.Release();
            }
        }

        /// <summary>
        /// Handles the provided unboxed <see cref="IGameServiceMessage"/> instance.
        /// </summary>
        public abstract void HandleMessage<T>(in T message) where T : struct, IGameServiceMessage;
    }
}