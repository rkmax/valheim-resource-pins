using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace ResourcePins;

/// <summary>
/// The registry file format. Unity's JsonUtility writes none of the registry's lists from a plugin assembly,
/// so we use Json.NET (it comes with the game). It is set to honour [Serializable]: it writes fields only and skips [NonSerialized].
/// </summary>
public static class RegistryJson
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new DefaultContractResolver { IgnoreSerializableAttribute = false },
    };

    public static string Write(RegistryData data) => JsonConvert.SerializeObject(data, Formatting.Indented, Settings);

    /// <summary>
    /// Reads a registry file. Serializable types are created without running their constructors, so lists
    /// missing from the file (older versions, hand edits) come back null and are filled in here.
    /// </summary>
    public static RegistryData Read(string json)
    {
        RegistryData data = JsonConvert.DeserializeObject<RegistryData>(json, Settings) ?? new RegistryData();
        data.Pins ??= new();
        data.Ignored ??= new();
        data.Gone ??= new();
        data.Pins.RemoveAll(p => p == null);
        foreach (PinRecord pin in data.Pins)
        {
            pin.Members ??= new();
            pin.Chests ??= new();
            pin.Members.RemoveAll(m => m == null);
            pin.Chests.RemoveAll(c => c == null);
        }
        return data;
    }
}
