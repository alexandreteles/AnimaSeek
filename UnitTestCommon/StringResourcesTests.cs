using Common;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace UnitTestCommon
{
    public class StringResourcesTests
    {
        [Test]
        public void Format_SupportsStringAndEnumKeys()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    StringResources.Format("search_results_count_filtered", 12, 48),
                    Is.EqualTo("Showing 12 of 48 results"));
                Assert.That(
                    StringResources.Format(StringKey.UserXIsOffline, "alice"),
                    Is.EqualTo("User alice is offline"));
            });
        }

        [Test]
        public void EveryPortableEnumKey_ExistsInCatalog()
        {
            string[] missingKeys = Enum.GetValues<StringKey>()
                .Where(key => !StringResources.TryGet(key.ToString(), out _))
                .Select(key => key.ToString())
                .ToArray();

            Assert.That(missingKeys, Is.Empty);
        }

        [Test]
        public void Get_DecodesAndroidApostropheEscapes()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    StringResources.Get("no_account"),
                    Does.StartWith("Don't have an account?"));
                Assert.That(
                    StringResources.Format("note_title", "Alice"),
                    Is.EqualTo("Alice's note"));
            });
        }

        [Test]
        public void Get_StripsAndroidPresentationMarkupButKeepsText()
        {
            Assert.Multiple(() =>
            {
                Assert.That(StringResources.Get("user_info_bio"), Is.EqualTo("User bio"));
                Assert.That(StringResources.Get("user_info_picture"), Is.EqualTo("Picture"));
                Assert.That(
                    StringResources.Get("error_image_too_large"),
                    Is.EqualTo("Image is too large. Please choose a smaller image. ( < 5MB)."));
            });
        }

        [Test]
        public void Get_PreservesNewlinesQuotesAndUnicode()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    StringResources.Get("privileges_more_info"),
                    Does.Contain("on.\n\nThey also"));
                Assert.That(
                    StringResources.Format("room_join_pending", "Lounge"),
                    Is.EqualTo("Joining \"Lounge\"…"));
                Assert.That(
                    StringResources.Format("speed_limit_kbs_total", 5),
                    Is.EqualTo("5 KB/s · total"));
            });
        }

        /// <summary>
        /// Guards the iOS head's literal catalog lookups, which throw at the moment the string is needed —
        /// often inside a background or failure path that no screen exercises during ordinary development.
        /// </summary>
        [Test]
        public void EveryLiteralKeyUsedByTheIosHead_ExistsInCatalog()
        {
            string iosRoot = Path.GetFullPath(
                Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "Seeker.iOS"));
            if (!Directory.Exists(iosRoot))
            {
                Assert.Ignore($"iOS sources not found at {iosRoot}. Skipping iOS catalog coverage.");
            }

            var lookup = new Regex(
                @"(?:AppStrings|StringResources)\.(?:Get|Format)\(\s*""([^""]+)""",
                RegexOptions.Compiled);
            string[] missingKeys = Directory
                .EnumerateFiles(iosRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                    !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .SelectMany(path => lookup.Matches(File.ReadAllText(path)).Select(match => match.Groups[1].Value))
                .Distinct(StringComparer.Ordinal)
                .Where(key => !StringResources.TryGet(key, out _))
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();

            Assert.That(missingKeys, Is.Empty);
        }

        [Test]
        public void MissingKeys_AreExplicit()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => StringResources.Get("not_a_real_resource"),
                    Throws.TypeOf<KeyNotFoundException>());
                Assert.That(
                    StringResources.TryGet("not_a_real_resource", out string value),
                    Is.False);
                Assert.That(value, Is.Empty);
            });
        }
    }
}
