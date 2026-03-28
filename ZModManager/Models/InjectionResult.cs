using System;

namespace ZModManager.Models;

public record InjectionResult(
    bool        Success,
    string      Message,
    RuntimeType Backend,
    DateTime    Timestamp
)
{
    public static InjectionResult Ok(string message, RuntimeType backend)
        => new(true,  message, backend, DateTime.Now);

    public static InjectionResult Fail(string message, RuntimeType backend)
        => new(false, message, backend, DateTime.Now);
}
