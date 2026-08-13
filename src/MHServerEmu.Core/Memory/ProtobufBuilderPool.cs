using Google.ProtocolBuffers;

namespace MHServerEmu.Core.Memory
{
    /// <summary>
    /// Contains settings for <see cref="ProtobufBuilderPool{T}"/> instances.
    /// </summary>
    public static class ProtobufBuilderPoolSettings
    {
        [ThreadStatic]
        public static bool UseThreadLocalStorage;
    }

    public class ProtobufBuilderPool<TBuilder> where TBuilder: class, IBuilderLite, new()
    {
        private static readonly BuilderPool _sharedPool = new(ObjectPoolFlags.None);

        [ThreadStatic]
        private static BuilderPool _threadLocalPool;

        public static TBuilder Get()
        {
            if (ProtobufBuilderPoolSettings.UseThreadLocalStorage)
            {
                _threadLocalPool ??= new(ObjectPoolFlags.ThreadLocal);
                return _threadLocalPool.Get();
            }
            else
            {
                lock (_sharedPool)
                    return _sharedPool.Get();
            }
        }

        public static ObjectPoolHandle<TBuilder> Get(out TBuilder builder)
        {
            if (ProtobufBuilderPoolSettings.UseThreadLocalStorage)
            {
                _threadLocalPool ??= new(ObjectPoolFlags.ThreadLocal);
                return _threadLocalPool.Get(out builder);
            }
            else
            {
                lock (_sharedPool)
                    return _sharedPool.Get(out builder);
            }
        }

        public static void Return(TBuilder builder)
        {
            if (ProtobufBuilderPoolSettings.UseThreadLocalStorage)
            {
                _threadLocalPool.Return(builder);
            }
            else
            {
                lock (_sharedPool)
                    _sharedPool.Return(builder);
            }
        }

        private sealed class BuilderPool : ObjectPool<TBuilder>
        {
            public BuilderPool(ObjectPoolFlags flags) : base(flags) { }

            protected override TBuilder Allocate()
            {
                return new();
            }

            protected override void OnReturn(TBuilder instance)
            {
                instance.WeakClear();
            }

            protected override int GetAllocationWarningThreshold()
            {
                return 32;
            }
        }
    }
}
