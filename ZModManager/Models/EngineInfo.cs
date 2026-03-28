namespace ZModManager.Models;

public enum EngineType
{
    UnityMono,
    UnityIL2CPP,
    UnrealEngine,
    Godot,
    Unknown,
}

public record EngineInfo(
    EngineType  Engine,
    string      Label,
    string      Recommendation,
    RuntimeType RuntimeType
);
