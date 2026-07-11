#if NETFRAMEWORK
// BCL members the code uses that exist on net8/net10 but not .NET Framework
// 4.8 (the AutoCAD 2021-2024 target). Compiled only for net48 so the real
// framework implementations win everywhere else. Language-level polyfills
// (init/required/attributes) come from PolySharp, not here -- this file is
// only for runtime library surface.
// Root namespace (not .Core) so extension lookup via enclosing-namespace
// rules reaches these from every AcLayerStandardizer.* file without usings.
namespace AcLayerStandardizer;

internal static class Net48Compat
{
    public static void Deconstruct<TKey, TValue>(
        this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
    {
        key = pair.Key;
        value = pair.Value;
    }

    public static bool TryAdd<TKey, TValue>(
        this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
    {
        if (dictionary.ContainsKey(key)) return false;
        dictionary.Add(key, value);
        return true;
    }

    public static TValue? GetValueOrDefault<TKey, TValue>(
        this Dictionary<TKey, TValue> dictionary, TKey key)
        => dictionary.TryGetValue(key, out var value) ? value : default;

    public static TValue GetValueOrDefault<TKey, TValue>(
        this Dictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue)
        => dictionary.TryGetValue(key, out var value) ? value : defaultValue;

    public static string[] Split(this string s, char separator, StringSplitOptions options)
        => s.Split(new[] { separator }, options);

    public static string[] Split(this string s, string separator, StringSplitOptions options)
        => s.Split(new[] { separator }, options);

    public static bool StartsWith(this string s, char value)
        => s.Length > 0 && s[0] == value;

    public static bool Contains(this string s, char value)
        => s.IndexOf(value) >= 0;
}
#endif
