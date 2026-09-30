using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AcLayerStandardizer.Matching;

namespace AcLayerStandardizer.Core;

public static class RustNativeBridge
{
    private const string DllName = "acad_layer_ffi.dll";

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string libname);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "acad_version")]
    private static extern IntPtr NativeVersion();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "acad_calculate_similarity")]
    private static extern double NativeCalculateSimilarity(IntPtr a, IntPtr b);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "acad_classify_layers_json")]
    private static extern IntPtr NativeClassifyLayersJson(IntPtr requestJson);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "acad_categorize_layers_json")]
    private static extern IntPtr NativeCategorizeLayersJson(IntPtr layersJson, IntPtr dictJson);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "acad_free_string")]
    private static extern void NativeFreeString(IntPtr ptr);

    private static readonly bool _isLoaded;
    private static readonly string? _version;

    static RustNativeBridge()
    {
        try
        {
            _isLoaded = TryInitialize();
            if (_isLoaded)
            {
                IntPtr vPtr = NativeVersion();
                _version = Utf8PtrToString(vPtr);
            }
        }
        catch
        {
            _isLoaded = false;
        }
    }

    public static bool IsAvailable => _isLoaded;
    public static string? Version => _version;
    public static string? InitializationError { get; private set; }

    private static bool TryInitialize()
    {
        // Probe locations:
        // 1. Same directory as this assembly
        // 2. runtimes/win-x64/native/acad_layer_ffi.dll
        // 3. Dev repo paths: rust/target/release or rust/target/debug
        string? asmLocation = null;
        try
        {
            asmLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }
        catch { }

        var baseDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(asmLocation)) baseDirs.Add(asmLocation!);
        if (!string.IsNullOrEmpty(AppDomain.CurrentDomain.BaseDirectory)) baseDirs.Add(AppDomain.CurrentDomain.BaseDirectory);
        try { baseDirs.Add(Directory.GetCurrentDirectory()); } catch { }

        List<string> probePaths = [];
        foreach (var b in baseDirs)
        {
            probePaths.Add(Path.Combine(b, DllName));
            probePaths.Add(Path.Combine(b, "runtimes", "win-x64", "native", DllName));
            probePaths.Add(Path.Combine(b, "rust", "target", "release", DllName));
            probePaths.Add(Path.Combine(b, "rust", "target", "debug", DllName));

            // Upward traversals
            string current = b;
            for (int i = 0; i < 6; i++)
            {
                probePaths.Add(Path.Combine(current, "rust", "target", "release", DllName));
                probePaths.Add(Path.Combine(current, "rust", "target", "debug", DllName));
                string? parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || parent == current) break;
                current = parent;
            }
        }

        foreach (var path in probePaths)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                {
                    IntPtr handle = LoadLibrary(fullPath);
                    if (handle != IntPtr.Zero)
                    {
                        return true;
                    }
                    InitializationError = $"LoadLibrary failed for {fullPath} with Win32Error={Marshal.GetLastWin32Error()}";
                }
            }
            catch (Exception ex)
            {
                InitializationError = ex.Message;
            }
        }

        // Try system PATH fallback
        try
        {
            IntPtr handle = LoadLibrary(DllName);
            return handle != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    public static double CalculateSimilarity(string a, string b)
    {
        if (!_isLoaded)
        {
            return HeuristicMatcher.CalculateSimilarity(a, b);
        }

        IntPtr aPtr = StringToUtf8Ptr(a);
        IntPtr bPtr = StringToUtf8Ptr(b);
        try
        {
            return NativeCalculateSimilarity(aPtr, bPtr);
        }
        finally
        {
            Marshal.FreeHGlobal(aPtr);
            Marshal.FreeHGlobal(bPtr);
        }
    }

    public static List<MatchResult>? TryClassify(
        IReadOnlyCollection<string> sourceLayers,
        IReadOnlyCollection<string> standardLayers,
        double minConfidence,
        Dictionary<string, string>? memoryMappings = null)
    {
        if (!_isLoaded) return null;

        var request = new
        {
            sourceLayers,
            standardLayers,
            minConfidence,
            memoryMappings = memoryMappings ?? []
        };

        string json = JsonSerializer.Serialize(request);
        IntPtr reqPtr = StringToUtf8Ptr(json);
        try
        {
            IntPtr resPtr = NativeClassifyLayersJson(reqPtr);
            if (resPtr == IntPtr.Zero) return null;

            try
            {
                string? resJson = Utf8PtrToString(resPtr);
                if (string.IsNullOrEmpty(resJson)) return null;

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                options.Converters.Add(new JsonStringEnumConverter());
                return JsonSerializer.Deserialize<List<MatchResult>>(resJson, options);
            }
            finally
            {
                NativeFreeString(resPtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(reqPtr);
        }
    }

    public static LayerCategorizationResult? TryCategorize(
        IEnumerable<string> layerNames,
        LayerDictionaryDefinition dict)
    {
        if (!_isLoaded) return null;

        string layersJson = JsonSerializer.Serialize(layerNames);
        string dictJson = JsonSerializer.Serialize(dict, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        IntPtr lPtr = StringToUtf8Ptr(layersJson);
        IntPtr dPtr = StringToUtf8Ptr(dictJson);
        try
        {
            IntPtr resPtr = NativeCategorizeLayersJson(lPtr, dPtr);
            if (resPtr == IntPtr.Zero) return null;

            try
            {
                string? resJson = Utf8PtrToString(resPtr);
                if (string.IsNullOrEmpty(resJson)) return null;

                var parsed = JsonSerializer.Deserialize<LayerCategorizationResultDto>(resJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (parsed is null) return null;

                var result = new LayerCategorizationResult();
                if (parsed.LayerTags is not null)
                {
                    foreach (var kvp in parsed.LayerTags)
                    {
                        result.LayerTags[kvp.Key] = new HashSet<string>(kvp.Value, StringComparer.OrdinalIgnoreCase);
                    }
                }
                if (parsed.AlwaysHidden is not null)
                {
                    foreach (var h in parsed.AlwaysHidden)
                    {
                        result.AlwaysHidden.Add(h);
                    }
                }
                if (parsed.VisibleCategories is not null)
                {
                    result.VisibleCategories.AddRange(parsed.VisibleCategories);
                }
                if (parsed.SortGroupByTag is not null)
                {
                    foreach (var kvp in parsed.SortGroupByTag)
                    {
                        result.SortGroupByTag[kvp.Key] = kvp.Value;
                    }
                }

                return result;
            }
            finally
            {
                NativeFreeString(resPtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(lPtr);
            Marshal.FreeHGlobal(dPtr);
        }
    }

    private class LayerCategorizationResultDto
    {
        public Dictionary<string, List<string>>? LayerTags { get; set; }
        public List<string>? AlwaysHidden { get; set; }
        public List<string>? VisibleCategories { get; set; }
        public Dictionary<string, string>? SortGroupByTag { get; set; }
    }

    private static IntPtr StringToUtf8Ptr(string str)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(str + "\0");
        IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        return ptr;
    }

    private static string? Utf8PtrToString(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return null;
        int len = 0;
        while (Marshal.ReadByte(ptr, len) != 0) len++;
        byte[] buffer = new byte[len];
        Marshal.Copy(ptr, buffer, 0, len);
        return Encoding.UTF8.GetString(buffer);
    }
}
