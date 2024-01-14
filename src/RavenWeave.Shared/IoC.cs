using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Ravenfall.Updater.Core
{

    public interface ITimeoutHandle
    {
    }

    public interface IKernel
    {
        ITimeoutHandle SetTimeout(Action action, int timeoutMilliseconds);
        void ClearTimeout(ITimeoutHandle discordBroadcast);

        void Start();
        void Stop();
        bool Started { get; }
    }
    public class Kernel : IKernel
    {
        private readonly object mutex = new object();
        private readonly object timeoutMutex = new object();
        private readonly List<TimeoutCallbackHandle> timeouts = new List<TimeoutCallbackHandle>();

        private Thread kernelThread;
        private bool started;

        public ITimeoutHandle SetTimeout(Action action, int timeoutMilliseconds)
        {
            lock (timeoutMutex)
            {
                var timeout = new TimeoutCallbackHandle(action, timeoutMilliseconds);
                this.timeouts.Add(timeout);
                return timeout;
            }
        }

        public void ClearTimeout(ITimeoutHandle handle)
        {
            lock (timeoutMutex)
            {
                this.timeouts.Remove((TimeoutCallbackHandle)handle);
            }
        }

        public void Start()
        {
            lock (mutex)
            {
                if (this.started)
                {
                    return;
                }

                this.started = true;
                this.kernelThread = new Thread(KernelProcess)
                {
                    IsBackground = true
                };
                this.kernelThread.Start();
            }
        }

        public void Stop()
        {
            lock (mutex)
            {
                this.started = false;
                this.kernelThread.Join();
            }
        }

        public bool Started => started;

        private void KernelProcess()
        {
            do
            {
                lock (mutex)
                {
                    if (!this.started)
                    {
                        return;
                    }
                }

                var timeout = 100;
                try
                {
                    lock (timeoutMutex)
                    {
                        var item = this.timeouts.OrderBy(x => x.Timeout).FirstOrDefault();
                        if (item != null)
                        {
                            if (DateTime.Now >= item.Timeout)
                            {
                                ClearTimeout(item);
                                item.Action?.Invoke();
                                continue;
                            }

                            timeout = Math.Min(timeout, (int)(item.Timeout - DateTime.Now).TotalMilliseconds);
                        }
                    }
                }
                catch
                {
                    // ignored, we can't have the kernel die due to an exception
                }
                System.Threading.Thread.Sleep(timeout);
            } while (true);
        }

        private class TimeoutCallbackHandle : ITimeoutHandle
        {
            public TimeoutCallbackHandle(Action action, int timeout)
            {
                this.Registered = DateTime.Now;
                this.Timeout = this.Registered.AddMilliseconds(timeout);
                this.Action = action;
            }

            public DateTime Registered { get; }
            public DateTime Timeout { get; }
            public Action Action { get; }
            public Guid Id { get; } = Guid.NewGuid();
            
        }
    }
    public class IoC : IDisposable
    {
        private readonly ConcurrentDictionary<Type, object> instances
            = new ConcurrentDictionary<Type, object>();

        private readonly ConcurrentDictionary<Type, TypeLookup> typeLookup
            = new ConcurrentDictionary<Type, TypeLookup>();

        private readonly ConcurrentDictionary<Type, Func<object>> typeFactories
            = new ConcurrentDictionary<Type, Func<object>>();

        public IoC RegisterShared<TInterface, TImplementation>()
        {
            typeLookup[typeof(TInterface)] = new TypeLookup(typeof(TImplementation), true);
            return this;
        }

        public IoC Register<TInterface, TImplementation>()
        {
            typeLookup[typeof(TInterface)] = new TypeLookup(typeof(TImplementation), false);
            return this;
        }

        public IoC Register<TImplementation>()
        {
            typeLookup[typeof(TImplementation)] = new TypeLookup(typeof(TImplementation), false);
            return this;
        }

        public IoC RegisterCustomShared<T>(Func<object> func)
        {
            typeLookup[typeof(T)] = new TypeLookup(typeof(T), true);
            typeFactories[typeof(T)] = func;
            return this;
        }

        public IoC RegisterCustom<T>(Func<object> func)
        {
            typeLookup[typeof(T)] = new TypeLookup(typeof(T), false);
            typeFactories[typeof(T)] = func;
            return this;
        }

        public TInterface Resolve<TInterface>(params object[] args)
        {
            return (TInterface)Resolve(typeof(TInterface), args);
        }

        public object Resolve(Type t, params object[] args)
        {
            var interfaceType = t;

            if (!typeLookup.TryGetValue(t, out var targetType))
                throw new Exception($"Unable to resolve the type {t.Name}");

            if (targetType.Shared)
            {
                if (instances.TryGetValue(t, out var obj))
                {
                    return obj;
                }
            }

            if (typeFactories.TryGetValue(interfaceType, out var factory))
            {
                var item = factory();
                instances[interfaceType] = item;
                return item;
            }

            var publicConstructors = targetType.Type
                .GetConstructors(BindingFlags.Public | BindingFlags.CreateInstance | BindingFlags.Instance);

            foreach (var ctor in publicConstructors)
            {
                var param = ctor.GetParameters();
                if (param.Length == 0)
                {
                    var instance = ctor.Invoke(null);
                    if (targetType.Shared) instances[interfaceType] = instance;
                    return instance;
                }

                var customArgIndex = 0;
                var hasCustomArgs = args.Length > 0;
                var badConstructor = false;
                var ctorArgs = new List<object>();
                foreach (var x in param)
                {
                    if (x.ParameterType.IsValueType || x.ParameterType == typeof(string))
                    {
                        if (!hasCustomArgs || args.Length <= customArgIndex)
                        {
                            badConstructor = true;
                            break;
                        }

                        ctorArgs.Add(args[customArgIndex++]);
                        continue;
                    }

                    ctorArgs.Add(Resolve(x.ParameterType));
                }

                if (badConstructor)
                {
                    continue;
                }

                var item = ctor.Invoke(ctorArgs.ToArray());
                if (targetType.Shared) instances[interfaceType] = item;
                return item;
            }
            throw new Exception($"Unable to resolve the type {targetType.Type.Name}");
        }

        public void Dispose()
        {
            foreach (var instance in instances.Values)
            {
                if (instance is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        private class TypeLookup
        {
            public TypeLookup(Type type, bool shared)
            {
                Type = type;
                Shared = shared;
            }

            public Type Type { get; }
            public bool Shared { get; }
        }
    }
}