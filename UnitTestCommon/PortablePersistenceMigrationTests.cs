using NUnit.Framework;
using Seeker;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UnitTestCommon
{
    [TestFixture]
    public sealed class PortablePersistenceMigrationTests
    {
        [Test]
        public void StringList_CurrentJson_RoundTripsWithoutReflectionSerialization()
        {
            var source = new[] { "first", "second & third", "first" };

            string payload = SerializationHelper.SaveStringListToString(source);
            List<string> restored = SerializationHelper.RestoreStringListFromString(payload);

            Assert.Multiple(() =>
            {
                Assert.That(payload.TrimStart(), Does.StartWith("["));
                Assert.That(payload, Does.Not.Contain("ArrayOfString"));
                Assert.That(restored, Is.EqualTo(source));
            });
        }

        [Test]
        public void StringList_LegacyXml_RemainsReadable()
        {
            const string payload =
                "<?xml version=\"1.0\"?><ArrayOfString><string>old value</string>" +
                "<string>escaped &amp; value</string></ArrayOfString>";

            List<string> restored = SerializationHelper.RestoreStringListFromString(payload);

            Assert.That(restored, Is.EqualTo(new[] { "old value", "escaped & value" }));
        }

        [TestCase("<ArrayOfString><string>unfinished")]
        [TestCase("<!DOCTYPE foo [<!ENTITY xxe SYSTEM 'file:///etc/passwd'>]><ArrayOfString>&xxe;</ArrayOfString>")]
        [TestCase("not-json-or-xml")]
        public void StringList_InvalidOrUnsafePayload_ReturnsEmpty(string payload)
        {
            Assert.That(SerializationHelper.RestoreStringListFromString(payload), Is.Empty);
        }

        [Test]
        public void SeekerSettings_CurrentJson_RoundTripsThroughImporter()
        {
            var export = new SeekerImportExportData
            {
                Userlist = new List<string> { "friend" },
                BanIgnoreList = new List<string> { "ignored" },
                Wishlist = new List<string> { "rare album" },
                UserNotes = new List<KeyValueEl>
                {
                    new KeyValueEl { Key = "friend", Value = "met at a show" },
                },
            };
            string payload = SerializationHelper.SerializeToString(export);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(" \n" + payload));

            ImportedData imported = ImportHelper.ImportFile("animaseek-settings.json", stream);

            Assert.Multiple(() =>
            {
                Assert.That(payload.TrimStart(), Does.StartWith("{"));
                Assert.That(imported.UserList, Is.EqualTo(new[] { "friend" }));
                Assert.That(imported.IgnoredBanned, Is.EqualTo(new[] { "ignored" }));
                Assert.That(imported.Wishlist, Is.EqualTo(new[] { "rare album" }));
                Assert.That(imported.UserNotes, Has.Count.EqualTo(1));
                Assert.That(imported.UserNotes[0].Item1, Is.EqualTo("friend"));
                Assert.That(imported.UserNotes[0].Item2, Is.EqualTo("met at a show"));
            });
        }

        [Test]
        public void SeekerSettings_CurrentJson_MissingOptionalCollectionsBecomeEmpty()
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"Userlist\":[\"friend\"]}"));

            ImportedData imported = ImportHelper.ImportFile("settings", stream);

            Assert.Multiple(() =>
            {
                Assert.That(imported.UserList, Is.EqualTo(new[] { "friend" }));
                Assert.That(imported.IgnoredBanned, Is.Empty);
                Assert.That(imported.Wishlist, Is.Empty);
                Assert.That(imported.UserNotes, Is.Empty);
            });
        }

        [Test]
        public void RecentUserManager_PersistsDetachedSnapshotOnlyWhenRequested()
        {
            IReadOnlyList<string>? persisted = null;
            var manager = new RecentUserManager(values => persisted = values);
            manager.SetRecentUserList(new[] { "older", "oldest" });

            manager.AddUserToTop("new", andSave: false);
            Assert.That(persisted, Is.Null);

            manager.AddUserToTop("older", andSave: true);
            List<string> mutableView = manager.GetRecentUserList();
            mutableView.Clear();

            Assert.Multiple(() =>
            {
                Assert.That(persisted, Is.EqualTo(new[] { "older", "new", "oldest" }));
                Assert.That(manager.GetRecentUserList(), Is.EqualTo(new[] { "older", "new", "oldest" }));
            });
        }
    }
}
