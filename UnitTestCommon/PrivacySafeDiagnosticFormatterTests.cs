using NUnit.Framework;
using Seeker.Helpers;
using System;

namespace UnitTestCommon
{
    [TestFixture]
    public sealed class PrivacySafeDiagnosticFormatterTests
    {
        [TestCase("alice")]
        [TestCase("/Users/alice/Documents/private/song.flac")]
        [TestCase("slsk://alice/folder/private.flac")]
        [TestCase("my secret chat message")]
        public void Format_DoesNotCopyPotentiallySensitiveInput(string sensitiveValue)
        {
            string line = PrivacySafeDiagnosticFormatter.Format(
                DateTimeOffset.UnixEpoch,
                "ERROR",
                $"Transfer failed for {sensitiveValue}",
                typeof(InvalidOperationException));

            Assert.That(line, Does.Not.Contain(sensitiveValue));
            Assert.That(line, Does.Contain("[ERROR] event="));
            Assert.That(line, Does.Contain("exception=System.InvalidOperationException"));
        }

        [Test]
        public void RedactLegacyLine_PreservesNoFreeText()
        {
            const string legacy = "2026-08-13T10:20:30.0000000+00:00 [ERROR] alice /private/file.mp3";

            string redacted = PrivacySafeDiagnosticFormatter.RedactLegacyLine(legacy);

            Assert.That(redacted, Does.Not.Contain("alice"));
            Assert.That(redacted, Does.Not.Contain("private"));
            Assert.That(redacted, Does.Not.Contain("file.mp3"));
            Assert.That(redacted, Does.Contain("[MIGRATED] event="));
        }

        [Test]
        public void Signature_IsStableAndFixedWidth()
        {
            string first = PrivacySafeDiagnosticFormatter.CreateSignature("same event");
            string second = PrivacySafeDiagnosticFormatter.CreateSignature("same event");

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Has.Length.EqualTo(16));
        }
    }
}
