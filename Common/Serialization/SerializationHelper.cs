using Seeker.Serialization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Xml;
using System.Xml.Linq;
using MessagePack;
using Soulseek;

namespace Seeker
{
    public class SerializationHelper
    {
        private static readonly MessagePackSerializerOptions AotMessagePackOptions =
            new(SeekerMessagePackResolver.Instance);

        /// <summary>Gets the AOT-safe options for browse response payloads.</summary>
        public static MessagePackSerializerOptions BrowseResponseOptions => AotMessagePackOptions;

        /// <summary>Gets the AOT-safe options for search response payloads.</summary>
        public static MessagePackSerializerOptions SearchResponseOptions => AotMessagePackOptions;

        /// <summary>Gets the AOT-safe options for private message payloads.</summary>
        public static MessagePackSerializerOptions MessageOptions => AotMessagePackOptions;

        /// <summary>Gets the AOT-safe options for user list payloads.</summary>
        public static MessagePackSerializerOptions UserListOptions => AotMessagePackOptions;

        /// <summary>Gets the AOT-safe options for share cache and primitive payloads.</summary>
        internal static MessagePackSerializerOptions CacheOptions => AotMessagePackOptions;

        /// <summary>
        /// Serializes a registered Seeker JSON persistence type without reflection.
        /// </summary>
        /// <typeparam name="T">A type registered by <see cref="SeekerJsonSerializerContext" />.</typeparam>
        /// <param name="objectToSerialize">The value to serialize.</param>
        /// <returns>The JSON representation of <paramref name="objectToSerialize" />.</returns>
        /// <exception cref="NotSupportedException"><typeparamref name="T" /> is not a registered persistence type.</exception>
        public static string SerializeToString<T>(T objectToSerialize)
        {
            return JsonSerializeToString<T>(objectToSerialize);
        }

        /// <summary>
        /// Deserializes a registered Seeker JSON persistence type without reflection.
        /// </summary>
        /// <typeparam name="T">A reference type registered by <see cref="SeekerJsonSerializerContext" />.</typeparam>
        /// <param name="serializedString">The JSON payload to deserialize.</param>
        /// <returns>The deserialized value.</returns>
        /// <exception cref="NotSupportedException"><typeparamref name="T" /> is not a registered persistence type.</exception>
        /// <exception cref="JsonException"><paramref name="serializedString" /> is not valid for <typeparamref name="T" />.</exception>
        public static T DeserializeFromString<T>(string serializedString) where T : class
        {
            return JsonDeserializeFromString<T>(serializedString);
        }

        private static T JsonDeserializeFromString<T>(string serializedString)
        {
            return JsonSerializer.Deserialize(serializedString, GetJsonTypeInfo<T>())!;
        }

        /// <summary>
        /// Serializes a registered Seeker JSON persistence type using source-generated metadata.
        /// </summary>
        /// <typeparam name="T">A type registered by <see cref="SeekerJsonSerializerContext" />.</typeparam>
        /// <param name="objectToSerialize">The value to serialize.</param>
        /// <returns>The JSON representation of <paramref name="objectToSerialize" />.</returns>
        /// <exception cref="NotSupportedException"><typeparamref name="T" /> is not a registered persistence type.</exception>
        public static string JsonSerializeToString<T>(T objectToSerialize)
        {
            return JsonSerializer.Serialize(objectToSerialize, GetJsonTypeInfo<T>());
        }

        private static JsonTypeInfo<T> GetJsonTypeInfo<T>()
        {
            if (SeekerJsonSerializerContext.Default.GetTypeInfo(typeof(T)) is JsonTypeInfo<T> typeInfo)
            {
                return typeInfo;
            }

            throw new NotSupportedException(
                $"{typeof(T).FullName} is not a registered Seeker JSON persistence type.");
        }

        /// <summary>
        /// Serializes a string list in Seeker's current trimming-safe JSON format.
        /// </summary>
        /// <param name="values">The values to serialize. Enumeration order and duplicates are preserved.</param>
        /// <returns>A JSON array containing <paramref name="values"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
        public static string SaveStringListToString(IEnumerable<string> values)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            return SerializeToString(new List<string>(values));
        }

        /// <summary>
        /// Restores a string list from current JSON or the legacy <c>ArrayOfString</c> XML format.
        /// </summary>
        /// <param name="serializedString">The persisted JSON or XML payload.</param>
        /// <returns>
        /// The restored values, or an empty list when the payload is empty, malformed, or is not a supported
        /// string-list document.
        /// </returns>
        /// <remarks>
        /// Legacy XML parsing prohibits DTD processing and external entity resolution. This method intentionally
        /// treats a damaged preference as empty so a single local persistence value cannot prevent app startup.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="serializedString"/> is <see langword="null"/>.
        /// </exception>
        public static List<string> RestoreStringListFromString(string serializedString)
        {
            if (serializedString == null)
            {
                throw new ArgumentNullException(nameof(serializedString));
            }

            string payload = serializedString.Trim();
            if (payload.Length == 0)
            {
                return new List<string>();
            }

            try
            {
                if (!payload.StartsWith("<", StringComparison.Ordinal))
                {
                    return DeserializeFromString<List<string>>(payload) ?? new List<string>();
                }

                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                };
                using var textReader = new StringReader(payload);
                using XmlReader xmlReader = XmlReader.Create(textReader, settings);
                XDocument document = XDocument.Load(xmlReader, LoadOptions.None);
                XElement? root = document.Root;
                if (root == null ||
                    !string.Equals(root.Name.LocalName, "ArrayOfString", StringComparison.Ordinal))
                {
                    return new List<string>();
                }

                return root
                    .Elements()
                    .Where(element => string.Equals(element.Name.LocalName, "string", StringComparison.Ordinal))
                    .Select(element => element.Value)
                    .ToList();
            }
            catch (Exception exception) when (exception is JsonException || exception is XmlException)
            {
                return new List<string>();
            }
        }

        public static string SaveUserNotesToString(ConcurrentDictionary<string, string> userNotes)
        {
            if (userNotes == null || userNotes.Keys.Count == 0)
            {
                return string.Empty;
            }
            else
            {
                return SerializeToString(userNotes);
            }
        }

        public static ConcurrentDictionary<string, string> RestoreUserNotesFromString(string base64userNotes)
        {
            if (base64userNotes == string.Empty)
            {
                return new ConcurrentDictionary<string, string>();
            }

            return DeserializeFromString<ConcurrentDictionary<string, string>>(base64userNotes);
        }



        public static string SaveUserOnlineAlertsToString(ConcurrentDictionary<string, byte> onlineAlertsDict)
        {
            if (onlineAlertsDict == null || onlineAlertsDict.Keys.Count == 0)
            {
                return string.Empty;
            }
            else
            {
                return SerializeToString(onlineAlertsDict);
            }
        }

        public static ConcurrentDictionary<string, byte> RestoreUserOnlineAlertsFromString(string base64onlineAlerts)
        {
            if (string.IsNullOrEmpty(base64onlineAlerts))
            {
                return new ConcurrentDictionary<string, byte>();
            }

            return DeserializeFromString<ConcurrentDictionary<string, byte>>(base64onlineAlerts);
        }



        public static string SaveUserListToString(List<UserListItem> userList)
        {
            if (userList == null || userList.Count == 0)
            {
                return string.Empty;
            }
            else
            {
                var bytes = MessagePack.MessagePackSerializer.Serialize(userList, options: UserListOptions);
                return Convert.ToBase64String(bytes);
            }
        }

        public static List<UserListItem> RestoreUserListFromString(string base64userList)
        {
            if (base64userList == string.Empty)
            {
                return new List<UserListItem>();
            }
            return MessagePack.MessagePackSerializer.Deserialize<List<UserListItem>>(
                Convert.FromBase64String(base64userList),
                options: UserListOptions);
        }


        public static string SaveSavedStateHeaderDictToString(Dictionary<int, SavedStateSearchTabHeader> savedTabHeaderStates)
        {
            return SerializeToString(savedTabHeaderStates);
        }

        public static Dictionary<int, SavedStateSearchTabHeader> RestoreSavedStateHeaderDictFromString(string savedTabHeaderString)
        {
            return DeserializeFromString<Dictionary<int, SavedStateSearchTabHeader>>(savedTabHeaderString);
        }


        public static string SaveAutoJoinRoomsListToString(ConcurrentDictionary<string, List<string>> autoJoinRoomNames)
        {
            return SerializeToString(autoJoinRoomNames);
        }

        public static ConcurrentDictionary<string, List<string>> RestoreAutoJoinRoomsListFromString(string joinedRooms)
        {
            return DeserializeFromString<ConcurrentDictionary<string, List<string>>>(joinedRooms);
        }


        public static string SaveNotifyRoomsListToString(ConcurrentDictionary<string, List<string>> notifyRoomsList)
        {
            return SerializeToString(notifyRoomsList);
        }

        public static ConcurrentDictionary<string, List<string>> RestoreNotifyRoomsListFromString(string notifyRoomsListString)
        {
            return DeserializeFromString<ConcurrentDictionary<string, List<string>>>(notifyRoomsListString);
        }

        public static string SaveLastReadCountsToString(ConcurrentDictionary<string, ConcurrentDictionary<string, int>> rootLastReadCounts)
        {
            return SerializeToString(rootLastReadCounts);
        }

        public static ConcurrentDictionary<string, ConcurrentDictionary<string, int>> RestoreLastReadCountsFromString(string lastReadCounts)
        {
            if (string.IsNullOrEmpty(lastReadCounts))
            {
                return new ConcurrentDictionary<string, ConcurrentDictionary<string, int>>();
            }
            else
            {
                return DeserializeFromString<ConcurrentDictionary<string, ConcurrentDictionary<string, int>>>(lastReadCounts);
            }
        }

        public static string SaveMessagesToString(ConcurrentDictionary<string, ConcurrentDictionary<string, List<Message>>> rootMessages)
        {
            var byteArray = MessagePack.MessagePackSerializer.Serialize(rootMessages, MessageOptions);
            return Convert.ToBase64String(byteArray);
        }

        public static ConcurrentDictionary<string, ConcurrentDictionary<string, List<Message>>> RestoreMessagesFromString(string rootMessagesString)
        {
            var bytesArray = Convert.FromBase64String(rootMessagesString);
            return MessagePack.MessagePackSerializer.Deserialize<ConcurrentDictionary<string, ConcurrentDictionary<string, List<Message>>>>(bytesArray, MessageOptions);
        }


        public static byte[] SaveSearchResponsesToByteArray(List<SearchResponse> responses)
        {

            var byteArray = MessagePack.MessagePackSerializer.Serialize(responses, options: SearchResponseOptions);
            return byteArray;
        }

        public static List<SearchResponse> RestoreSearchResponsesFromStream(System.IO.Stream inputStream)
        {
                return MessagePack.MessagePackSerializer.Deserialize<List<SearchResponse>>(inputStream, options: SearchResponseOptions);
        }

        public static byte[] SaveUnseenIndicesToByteArray(int[] indices)
        {
            return MessagePack.MessagePackSerializer.Serialize(indices, CacheOptions);
        }

        public static int[] RestoreUnseenIndicesFromStream(System.IO.Stream inputStream)
        {
            return MessagePack.MessagePackSerializer.Deserialize<int[]>(inputStream, CacheOptions);
        }
    }
}
