using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text.Json;

namespace TextureUnshuffle.Models.UvAnalysis;

/// <summary>
/// Loads UV triangles from 3D model files.
/// Supported formats: .obj (built-in parser), .gltf/.glb (SharpGLTF).
/// </summary>
public static class ModelLoader
{
    public static List<UvTriangle> LoadUvTriangles(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".obj"            => LoadObj(path),
            ".gltf" or ".glb" => LoadGltf(path),
            _ => throw new NotSupportedException(
                $"Формат «{ext}» не поддерживается. Используйте .obj, .gltf или .glb.")
        };
    }

    // ── OBJ parser ────────────────────────────────────────────────────────────

    private static List<UvTriangle> LoadObj(string path)
    {
        var texCoords = new List<Vector2>();
        var triangles = new List<UvTriangle>();

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.TrimStart();

            if (line.StartsWith("vt ", StringComparison.Ordinal))
            {
                // vt u [v [w]]
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 &&
                    TryParseFloat(parts[1], out float u) &&
                    TryParseFloat(parts[2], out float v))
                {
                    texCoords.Add(new Vector2(u, v));
                }
            }
            else if (line.StartsWith("f ", StringComparison.Ordinal))
            {
                // f v/vt/vn  or  v//vn  or  v/vt  or  v
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var uvIndices = new List<int>(parts.Length - 1);

                for (int i = 1; i < parts.Length; i++)
                {
                    var tokens = parts[i].Split('/');
                    if (tokens.Length >= 2 &&
                        !string.IsNullOrEmpty(tokens[1]) &&
                        int.TryParse(tokens[1], out int idx))
                    {
                        uvIndices.Add(idx > 0 ? idx - 1 : texCoords.Count + idx);
                    }
                    else
                    {
                        uvIndices.Add(-1);
                    }
                }

                // Fan triangulation: (0,1,2), (0,2,3), …
                for (int i = 1; i + 1 < uvIndices.Count; i++)
                {
                    int i0 = uvIndices[0], i1 = uvIndices[i], i2 = uvIndices[i + 1];
                    if (i0 < 0 || i1 < 0 || i2 < 0) continue;
                    if (i0 >= texCoords.Count || i1 >= texCoords.Count || i2 >= texCoords.Count) continue;
                    triangles.Add(new UvTriangle(texCoords[i0], texCoords[i1], texCoords[i2]));
                }
            }
        }

        return triangles;
    }

    // ── GLTF / GLB parser (SharpGLTF) ────────────────────────────────────────

    private static readonly SharpGLTF.Schema2.ReadSettings GltfSettings = new()
    {
        Validation = SharpGLTF.Validation.ValidationMode.Skip
    };

    private static List<UvTriangle> LoadGltf(string path)
    {
        SharpGLTF.Schema2.ModelRoot model;
        try
        {
            model = SharpGLTF.Schema2.ModelRoot.Load(path, GltfSettings);
        }
        catch (SharpGLTF.Validation.SchemaException)
        {
            // Some exporters (e.g. Sketchfab Utility Ripper) emit empty JSON arrays []
            // for properties that the spec requires to be absent when empty.
            // Strip those properties and retry from a temp file in the same directory
            // so that relative buffer/texture paths still resolve correctly.
            model = LoadGltfFixed(path);
        }

        return ExtractGltfTriangles(model);
    }

    private static SharpGLTF.Schema2.ModelRoot LoadGltfFixed(string originalPath)
    {
        var dir      = Path.GetDirectoryName(originalPath)!;
        var tempName = "__fixed_" + Path.GetFileName(originalPath);
        var tempPath = Path.Combine(dir, tempName);
        try
        {
            var jsonBytes = File.ReadAllBytes(originalPath);
            using var doc = JsonDocument.Parse(jsonBytes);
            using (var fs = File.Create(tempPath))
            using (var writer = new Utf8JsonWriter(fs))
            {
                WriteWithoutEmptyArrays(writer, doc.RootElement);
            }
            return SharpGLTF.Schema2.ModelRoot.Load(tempPath, GltfSettings);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>Recursively copies a JsonElement, dropping object properties whose value is [].</summary>
    private static void WriteWithoutEmptyArrays(Utf8JsonWriter w, JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                w.WriteStartObject();
                foreach (var prop in e.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array &&
                        prop.Value.GetArrayLength() == 0) continue; // ← the fix
                    w.WritePropertyName(prop.Name);
                    WriteWithoutEmptyArrays(w, prop.Value);
                }
                w.WriteEndObject();
                break;
            case JsonValueKind.Array:
                w.WriteStartArray();
                foreach (var item in e.EnumerateArray())
                    WriteWithoutEmptyArrays(w, item);
                w.WriteEndArray();
                break;
            default:
                e.WriteTo(w);
                break;
        }
    }

    private static List<UvTriangle> ExtractGltfTriangles(SharpGLTF.Schema2.ModelRoot model)
    {
        var triangles = new List<UvTriangle>();
        foreach (var mesh in model.LogicalMeshes)
        {
            foreach (var prim in mesh.Primitives)
            {
                var uvAccessor = prim.GetVertexAccessor("TEXCOORD_0");
                if (uvAccessor == null) continue;

                var uvs     = uvAccessor.AsVector2Array();
                var indices = prim.GetIndices();

                if (indices == null)
                {
                    for (int i = 0; i + 2 < uvs.Count; i += 3)
                        triangles.Add(new UvTriangle(uvs[i], uvs[i + 1], uvs[i + 2]));
                }
                else
                {
                    for (int i = 0; i + 2 < indices.Count; i += 3)
                        triangles.Add(new UvTriangle(
                            uvs[(int)indices[i]],
                            uvs[(int)indices[i + 1]],
                            uvs[(int)indices[i + 2]]));
                }
            }
        }
        return triangles;
    }

    private static bool TryParseFloat(string s, out float value)
        => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
