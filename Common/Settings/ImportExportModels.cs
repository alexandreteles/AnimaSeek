using System;
using System.Collections.Generic;

namespace Seeker
{
    public struct ImportedData
    {
        public ImportedData(List<string> userList, List<string> ignoreBanned, List<string> wishlist, List<Tuple<string, string>> userNotes)
        {
            UserList = userList; IgnoredBanned = ignoreBanned; Wishlist = wishlist; UserNotes = userNotes;
        }

        public List<string> Wishlist { private set; get; }
        public List<string> UserList { private set; get; }
        public List<string> IgnoredBanned { private set; get; }
        public List<Tuple<string, string>> UserNotes { private set; get; }
    }

    /// <summary>Represents Seeker's portable settings-export schema.</summary>
    /// <remarks>
    /// Current exports use source-generated JSON. The field names remain identical to the legacy XML
    /// schema so older exports can continue to be imported without a lossy migration step.
    /// </remarks>
    [Serializable]
    public class SeekerImportExportData
    {
        public List<string> Wishlist = new List<string>();
        public List<string> Userlist = new List<string>();
        public List<string> BanIgnoreList = new List<string>();
        public List<KeyValueEl> UserNotes = new List<KeyValueEl>();
        // Legacy XML readers ignore unknown elements, so the export schema can grow additively.
    }

    /// <summary>Represents one user-note entry in a settings export.</summary>
    [Serializable]
    public class KeyValueEl
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
