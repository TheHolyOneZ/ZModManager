using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ZModManager.Models;

namespace ZModManager.Services;

/// <summary>
/// Inspects a mod DLL's assembly references to determine which mod framework it
/// was built for, without loading or executing the DLL.
/// Results are cached in memory — call <see cref="Invalidate"/> when a mod's
/// DLL path changes.
/// </summary>
public static class ModFrameworkDetector
{
    private static readonly Dictionary<string, DetectedModFramework> _cache = new();
    private static readonly object _cacheLock = new();

    /// <summary>
    /// Returns the detected framework for the DLL at <paramref name="dllPath"/>.
    /// Returns <see cref="DetectedModFramework.Unknown"/> if the path is empty or
    /// the file cannot be read.
    /// </summary>
    public static DetectedModFramework Detect(string dllPath)
    {
        if (string.IsNullOrWhiteSpace(dllPath)) return DetectedModFramework.Unknown;

        lock (_cacheLock)
        {
            if (_cache.TryGetValue(dllPath, out var cached)) return cached;
        }

        var result = DetectCore(dllPath);

        lock (_cacheLock)
        {
            _cache[dllPath] = result;
        }
        return result;
    }

    /// <summary>Removes a cached entry so the DLL is re-inspected on next call.</summary>
    public static void Invalidate(string dllPath)
    {
        if (string.IsNullOrWhiteSpace(dllPath)) return;
        lock (_cacheLock) { _cache.Remove(dllPath); }
    }

    private static DetectedModFramework DetectCore(string dllPath)
    {
        if (!File.Exists(dllPath)) return DetectedModFramework.Unknown;
        try
        {
            using var stream = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new PEReader(stream);

            if (!reader.HasMetadata) return DetectedModFramework.Native;

            var meta = reader.GetMetadataReader();

            bool hasMelonLoader = false;
            bool hasBepInEx     = false;

            foreach (var handle in meta.AssemblyReferences)
            {
                var name = meta.GetString(meta.GetAssemblyReference(handle).Name);

                // MelonLoader indicators: direct reference OR MelonLoader-published packages
                if (name.StartsWith("MelonLoader",   StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Melonix",           StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("MelonLoader.ModHandler", StringComparison.OrdinalIgnoreCase))
                    hasMelonLoader = true;

                // BepInEx indicators: core, Unity or IL2CPP-specific packages
                if (name.StartsWith("BepInEx",       StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Chainloader",       StringComparison.OrdinalIgnoreCase))
                    hasBepInEx = true;
            }

            // BepInEx takes precedence if both somehow referenced (unusual)
            if (hasBepInEx)     return DetectedModFramework.BepInEx;
            if (hasMelonLoader) return DetectedModFramework.MelonLoader;

            // Also check custom attributes — MelonLoader mods declare [assembly: MelonInfo(...)]
            // which means MelonLoader.MelonInfoAttribute will appear in the type refs
            var typeRefResult = ScanTypeRefs(meta);
            if (typeRefResult != DetectedModFramework.Unknown)
                return typeRefResult;

            return DetectedModFramework.Managed;
        }
        catch
        {
            // BadImageFormatException (mixed-mode DLL), UnauthorizedAccessException, etc.
            return DetectedModFramework.Unknown;
        }
    }

    /// <summary>
    /// Secondary detection pass: look for well-known type references in the metadata
    /// that reveal the framework even when the assembly reference name alone is not enough.
    /// </summary>
    private static DetectedModFramework ScanTypeRefs(MetadataReader meta)
    {
        foreach (var handle in meta.TypeReferences)
        {
            var typeRef  = meta.GetTypeReference(handle);
            var ns       = meta.GetString(typeRef.Namespace);
            var typeName = meta.GetString(typeRef.Name);

            // MelonLoader attribute used on every mod assembly
            if (ns.StartsWith("MelonLoader", StringComparison.OrdinalIgnoreCase))
                return DetectedModFramework.MelonLoader;

            // BepInEx base plugin attribute / class
            if (ns.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase))
                return DetectedModFramework.BepInEx;

            // Older MelonLoader: base class was just "MelonMod" in root namespace
            if (string.IsNullOrEmpty(ns) &&
                (typeName.Equals("MelonMod",  StringComparison.OrdinalIgnoreCase) ||
                 typeName.Equals("MelonInfo", StringComparison.OrdinalIgnoreCase)))
                return DetectedModFramework.MelonLoader;
        }
        return DetectedModFramework.Unknown;
    }
}
