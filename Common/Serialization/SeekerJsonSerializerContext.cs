using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Seeker.Serialization
{
    /// <summary>
    /// Provides trimming-safe JSON metadata for every JSON payload persisted by Seeker.
    /// </summary>
    [JsonSourceGenerationOptions(IncludeFields = true)]
    [JsonSerializable(typeof(List<UploadDirectoryInfo>))]
    [JsonSerializable(typeof(ConcurrentDictionary<string, string>))]
    [JsonSerializable(typeof(ConcurrentDictionary<string, byte>))]
    [JsonSerializable(typeof(Dictionary<int, SavedStateSearchTabHeader>))]
    [JsonSerializable(typeof(ConcurrentDictionary<string, List<string>>))]
    [JsonSerializable(typeof(ConcurrentDictionary<string, ConcurrentDictionary<string, int>>))]
    [JsonSerializable(typeof(List<TransferItem>))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSerializable(typeof(SeekerImportExportData))]
    internal partial class SeekerJsonSerializerContext : JsonSerializerContext
    {
    }
}
