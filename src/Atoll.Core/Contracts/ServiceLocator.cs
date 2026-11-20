using System;

namespace Atoll.Core.Contracts;

/// <summary>
/// Static service locator to break circular dependencies between projects.
/// App sets Services after building the DI container.
/// </summary>
public static class ServiceLocator
{
    public static IServiceProvider? Services { get; set; }
}
