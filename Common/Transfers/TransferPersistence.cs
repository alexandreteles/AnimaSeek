using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Soulseek;

namespace Seeker
{
    /// <summary>
    /// Persists transfer snapshots as trimming-safe JSON and restores both current JSON and legacy Android XML.
    /// </summary>
    public static class TransferPersistence
    {
        private static DateTime transfersLastSavedTime = DateTime.MinValue;

        /// <summary>Restores upload transfers from a current or legacy persisted payload.</summary>
        /// <param name="transferListLegacy">
        /// The primary transfer payload. New writes are JSON; existing Android installations may provide XML.
        /// </param>
        /// <param name="transferListV2">An older manager-shaped XML payload, when present.</param>
        public static void RestoreUploadTransferItems(string transferListLegacy, string transferListV2)
        {
            TransferItems.TransferItemManagerUploads = RestoreManager(
                string.IsNullOrWhiteSpace(transferListV2) ? transferListLegacy : transferListV2,
                isUploads: true);
        }

        /// <summary>Restores an older upload transfer-list payload.</summary>
        /// <param name="transferList">A JSON list or legacy XML transfer list.</param>
        public static void RestoreUploadTransferItemsLegacy(string transferList)
        {
            TransferItems.TransferItemManagerUploads = RestoreManager(transferList, isUploads: true);
        }

        /// <summary>Restores download transfers from a current or legacy persisted payload.</summary>
        /// <param name="transferListLegacy">
        /// The primary transfer payload. New writes are JSON; existing Android installations may provide XML.
        /// </param>
        /// <param name="transferListV2">An older manager-shaped XML payload, when present.</param>
        public static void RestoreDownloadTransferItems(string transferListLegacy, string transferListV2)
        {
            TransferItems.TransferItemManagerDL = RestoreManager(
                string.IsNullOrWhiteSpace(transferListV2) ? transferListLegacy : transferListV2,
                isUploads: false);
        }

        /// <summary>Restores an older download transfer-list payload.</summary>
        /// <param name="transferList">A JSON list or legacy XML transfer list.</param>
        public static void RestoreDownloadTransferItemsLegacy(string transferList)
        {
            TransferItems.TransferItemManagerDL = RestoreManager(transferList, isUploads: false);
        }

        /// <summary>
        /// Serializes consistent download and upload snapshots when transfer state is dirty or saving is forced.
        /// </summary>
        /// <param name="force">When <see langword="true"/>, saves even when state is clean or throttled.</param>
        /// <param name="maxSecondsUpdate">Minimum interval between non-forced saves.</param>
        /// <returns>JSON download/upload payloads, or <see langword="null"/> when saving was skipped.</returns>
        public static (string downloads, string uploads)? SaveTransferItems(
            bool force = false,
            int maxSecondsUpdate = 0)
        {
            if (!force &&
                (!TransferItemManager.TransfersDirty ||
                 DateTime.UtcNow.Subtract(transfersLastSavedTime).TotalSeconds <= maxSecondsUpdate))
            {
                return null;
            }

            if (TransferItems.TransferItemManagerDL?.AllTransferItems is null ||
                TransferItems.TransferItemManagerUploads?.AllTransferItems is null)
            {
                return null;
            }

            List<TransferItem> downloads;
            lock (TransferItems.TransferItemManagerDL.AllTransferItems)
            {
                downloads = TransferItems.TransferItemManagerDL.AllTransferItems.ToList();
            }

            List<TransferItem> uploads;
            lock (TransferItems.TransferItemManagerUploads.AllTransferItems)
            {
                uploads = TransferItems.TransferItemManagerUploads.AllTransferItems.ToList();
            }

            string serializedDownloads = SerializationHelper.SerializeToString(downloads);
            string serializedUploads = SerializationHelper.SerializeToString(uploads);

            TransferItemManager.TransfersDirty = false;
            transfersLastSavedTime = DateTime.UtcNow;
            return (serializedDownloads, serializedUploads);
        }

        private static TransferItemManager RestoreManager(string serialized, bool isUploads)
        {
            var manager = new TransferItemManager(isUploads);
            foreach (TransferItem item in DeserializeItems(serialized))
            {
                manager.Add(item);
            }

            manager.OnRelaunch();
            return manager;
        }

        private static IReadOnlyCollection<TransferItem> DeserializeItems(string serialized)
        {
            if (string.IsNullOrWhiteSpace(serialized))
            {
                return Array.Empty<TransferItem>();
            }

            string payload = serialized.TrimStart();
            if (payload.StartsWith("<", StringComparison.Ordinal))
            {
                return DeserializeLegacyXml(payload);
            }

            return SerializationHelper.DeserializeFromString<List<TransferItem>>(payload)
                   ?? throw new InvalidDataException("The transfer-state JSON payload was empty.");
        }

        private static IReadOnlyCollection<TransferItem> DeserializeLegacyXml(string serialized)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            };

            using var textReader = new StringReader(serialized);
            using XmlReader xmlReader = XmlReader.Create(textReader, settings);
            XDocument document = XDocument.Load(xmlReader, LoadOptions.None);
            IEnumerable<XElement> itemElements = document.Root is { } root &&
                                                 root.Name.LocalName == nameof(TransferItemManager)
                ? root.Elements()
                    .FirstOrDefault(element => element.Name.LocalName == nameof(TransferItemManager.AllTransferItems))?
                    .Elements()
                    .Where(element => element.Name.LocalName == nameof(TransferItem))
                    ?? Enumerable.Empty<XElement>()
                : document
                    .Descendants()
                    .Where(element => element.Name.LocalName == nameof(TransferItem));

            return itemElements
                .Select(ReadLegacyTransferItem)
                .ToArray();
        }

        private static TransferItem ReadLegacyTransferItem(XElement element)
        {
            var item = new TransferItem
            {
                Filename = ReadString(element, nameof(TransferItem.Filename)),
                Username = ReadString(element, nameof(TransferItem.Username)),
                FolderName = ReadString(element, nameof(TransferItem.FolderName)),
                FullFilename = ReadString(element, nameof(TransferItem.FullFilename)),
                Progress = ReadInt32(element, nameof(TransferItem.Progress)),
                BytesTransferred = ReadInt64(element, nameof(TransferItem.BytesTransferred)),
                Failed = ReadBoolean(element, nameof(TransferItem.Failed)),
                State = ReadEnum(element, nameof(TransferItem.State), TransferStates.None),
                Size = ReadInt64(element, nameof(TransferItem.Size)),
                isUpload = ReadBoolean(element, nameof(TransferItem.isUpload)),
                CancelAndRetryFlag = ReadBoolean(element, nameof(TransferItem.CancelAndRetryFlag)),
                WasFilenameLatin1Decoded = ReadBoolean(element, nameof(TransferItem.WasFilenameLatin1Decoded)),
                WasFolderLatin1Decoded = ReadBoolean(element, nameof(TransferItem.WasFolderLatin1Decoded)),
                FinalUri = ReadString(element, nameof(TransferItem.FinalUri), string.Empty),
                IncompleteParentUri = ReadNullableString(element, nameof(TransferItem.IncompleteParentUri)),
                IncompleteUri = ReadNullableString(element, nameof(TransferItem.IncompleteUri)),
                TransferItemExtra = ReadEnum(
                    element,
                    nameof(TransferItem.TransferItemExtra),
                    default(Transfers.TransferItemExtras)),
                AvgSpeed = ReadDouble(element, nameof(TransferItem.AvgSpeed)),
            };

            XElement? queueLength = FindElement(element, nameof(TransferItem.QueueLength));
            if (queueLength is not null && !string.IsNullOrWhiteSpace(queueLength.Value))
            {
                item.QueueLength = XmlConvert.ToInt32(queueLength.Value);
            }

            return item;
        }

        private static XElement? FindElement(XElement parent, string name) =>
            parent.Elements().FirstOrDefault(element => element.Name.LocalName == name);

        private static string ReadString(XElement parent, string name, string defaultValue = "") =>
            ReadNullableString(parent, name) ?? defaultValue;

        private static string? ReadNullableString(XElement parent, string name)
        {
            XElement? element = FindElement(parent, name);
            if (element is null ||
                string.Equals(
                    element.Attribute(XName.Get("nil", "http://www.w3.org/2001/XMLSchema-instance"))?.Value,
                    "true",
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return element.Value;
        }

        private static int ReadInt32(XElement parent, string name)
        {
            string? value = ReadNullableString(parent, name);
            return string.IsNullOrWhiteSpace(value) ? 0 : XmlConvert.ToInt32(value);
        }

        private static long ReadInt64(XElement parent, string name)
        {
            string? value = ReadNullableString(parent, name);
            return string.IsNullOrWhiteSpace(value) ? 0 : XmlConvert.ToInt64(value);
        }

        private static double ReadDouble(XElement parent, string name)
        {
            string? value = ReadNullableString(parent, name);
            return string.IsNullOrWhiteSpace(value) ? 0 : XmlConvert.ToDouble(value);
        }

        private static bool ReadBoolean(XElement parent, string name)
        {
            string? value = ReadNullableString(parent, name);
            return !string.IsNullOrWhiteSpace(value) && XmlConvert.ToBoolean(value.ToLowerInvariant());
        }

        private static TEnum ReadEnum<TEnum>(XElement parent, string name, TEnum defaultValue)
            where TEnum : struct, Enum
        {
            string? value = ReadNullableString(parent, name);
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number))
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), number);
            }

            string flags = string.Join(", ", value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return Enum.TryParse(flags, ignoreCase: false, out TEnum parsed) ? parsed : defaultValue;
        }
    }
}
