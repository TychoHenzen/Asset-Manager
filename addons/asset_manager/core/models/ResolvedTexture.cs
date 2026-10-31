namespace AssetManager.Core;

/// Where a model's texture reference was found. `Exact` means the file has
/// the referenced name, so Godot's own importer finds it too once it sits
/// where the importer looks; a fallback match (another extension, a numbered
/// variant) needs the material linked explicitly.
public sealed record ResolvedTexture(string Reference, string Path, bool Exact);
