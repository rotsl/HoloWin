using System.Collections.Concurrent;

namespace HoloWin.Core;

/// <summary>
/// A simple, fast, dependency-free DI container. Resolves constructors by
/// registered type. Not a replacement for Microsoft.Extensions.DependencyInjection
/// when richer DI is required; suitable for the HoloWin modules that need to
/// compose their graphs at startup without external packages.
/// </summary>
public sealed class ServiceContainer
{
    private readonly ConcurrentDictionary<Type, Func<ServiceContainer, object>> _factories = new();
    private readonly ConcurrentDictionary<Type, object> _singletons = new();
    private readonly ConcurrentDictionary<Type, bool> _inCreation = new();
    private readonly object _lock = new();

    public void AddSingleton<T>(Func<ServiceContainer, T> factory) where T : class
    {
        var t = typeof(T);
        if (_factories.ContainsKey(t))
            throw new InvalidOperationException($"Type {t} already registered.");
        _factories[t] = c => factory(c) ?? throw new InvalidOperationException($"Factory for {t} returned null.");
    }

    public void AddSingleton<T>(T instance) where T : class
    {
        var t = typeof(T);
        if (_factories.ContainsKey(t))
            throw new InvalidOperationException($"Type {t} already registered.");
        _factories[t] = _ => instance;
        _singletons[t] = instance;
    }

    public void AddTransient<T>(Func<ServiceContainer, T> factory) where T : class
    {
        var t = typeof(T);
        _factories[t] = c => factory(c) ?? throw new InvalidOperationException($"Factory for {t} returned null.");
    }

    public T Resolve<T>() where T : class
    {
        var t = typeof(T);
        if (_singletons.TryGetValue(t, out var existing)) return (T)existing;
        if (!_factories.TryGetValue(t, out var factory))
            throw new InvalidOperationException($"Type {t} not registered.");
        if (!_inCreation.TryAdd(t, true))
            throw new InvalidOperationException($"Circular dependency detected resolving {t}.");
        try
        {
            var instance = factory(this);
            _singletons[t] = instance;
            return (T)instance;
        }
        finally
        {
            _inCreation.TryRemove(t, out _);
        }
    }

    public bool TryResolve<T>(out T? value) where T : class
    {
        var t = typeof(T);
        if (_singletons.TryGetValue(t, out var existing))
        {
            value = (T)existing;
            return true;
        }
        if (!_factories.TryGetValue(t, out var factory))
        {
            value = null;
            return false;
        }
        try
        {
            value = (T)factory(this);
            return true;
        }
        catch
        {
            value = null;
            return false;
        }
    }
}
