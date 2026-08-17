using Seeker.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnitTestCommon
{
    internal sealed class TestKeyValueStore : IKeyValueStore
    {
        private readonly Dictionary<string, object> values = new(StringComparer.Ordinal);

        public int FlushCount { get; private set; }

        public IReadOnlyCollection<string> Keys => values.Keys;

        public bool GetBoolean(string key, bool defaultValue) => GetValue(key, defaultValue);

        public int GetInt(string key, int defaultValue) => GetValue(key, defaultValue);

        public long GetLong(string key, long defaultValue) => GetValue(key, defaultValue);

        public string? GetString(string key, string? defaultValue = null) => GetValue(key, defaultValue);

        public IReadOnlyCollection<string>? GetStringSet(
            string key,
            IReadOnlyCollection<string>? defaultValue = null) =>
            values.TryGetValue(key, out object? value)
                ? ((IReadOnlyCollection<string>)value).ToArray()
                : defaultValue;

        public void PutBoolean(string key, bool value) => values[key] = value;

        public void PutInt(string key, int value) => values[key] = value;

        public void PutLong(string key, long value) => values[key] = value;

        public void PutString(string key, string? value) => PutNullable(key, value);

        public void PutStringSet(string key, IReadOnlyCollection<string>? value) =>
            PutNullable(key, value?.Distinct(StringComparer.Ordinal).ToArray());

        public void Flush() => FlushCount++;

        private T GetValue<T>(string key, T defaultValue) =>
            values.TryGetValue(key, out object? value) ? (T)value : defaultValue;

        private void PutNullable(string key, object? value)
        {
            if (value == null)
            {
                values.Remove(key);
            }
            else
            {
                values[key] = value;
            }
        }
    }
}
