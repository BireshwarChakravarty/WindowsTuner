using System.Runtime.Versioning;

// The engine is Windows-only at runtime. Pure helpers (parsers, tweak engine, recommendation rules)
// are still callable from the cross-platform unit tests.
[assembly: SupportedOSPlatform("windows")]
