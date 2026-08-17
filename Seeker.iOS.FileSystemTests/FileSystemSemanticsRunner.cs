using System.Text;
using AnimaSeek.iOS.Services;
using Common;
using Foundation;

namespace AnimaSeek.iOS.FileSystemTests;

/// <summary>
/// Exercises the production iOS filesystem implementation inside a real simulator application sandbox.
/// </summary>
internal static class FileSystemSemanticsRunner
{
    internal const string ResultFileName = "filesystem-semantics-result.txt";
    internal const string SuccessMarker = "IOS_FS_SEMANTICS_PASS";
    internal const string FailureMarker = "IOS_FS_SEMANTICS_FAIL";

    /// <summary>
    /// Runs all filesystem checks, persists a machine-readable report, and returns a process exit code.
    /// </summary>
    /// <returns>Zero when every check and final cleanup succeeds; otherwise one.</returns>
    public static int Run()
    {
        var fileSystem = new IosFileSystemService();
        string resultPath = Path.Combine(fileSystem.ApplicationSupportPath, ResultFileName);
        File.Delete(resultPath);

        bool originalUsernameSubfolders = PreferencesState.CreateUsernameSubfolders;
        PreferencesState.CreateUsernameSubfolders = false;

        var passed = new List<string>();
        var failures = new List<string>();
        (string Name, Action<IosFileSystemService> Test)[] tests =
        [
            ("append-reopen-position", AppendAndReopenPreservesPosition),
            ("resume-actual-partial-length", ResumeUsesActualPartialLength),
            ("private-partial-identity", PrivatePartialsUseCompleteTransferIdentity),
            ("finalize-move-bytes", FinalizationMovesExactBytes),
            ("production-finalize-collision-containment", ProductionFinalizationCollisionAndTraversalStayContained),
            ("user-defaults-roundtrip-durable-flush", UserDefaultsRoundTripAndFlushFailure),
            ("cleanup", CleanupRemovesPartialAndEmptyParents),
        ];

        try
        {
            foreach ((string name, Action<IosFileSystemService> test) in tests)
            {
                try
                {
                    test(fileSystem);
                    passed.Add(name);
                }
                catch (Exception exception)
                {
                    failures.Add($"{name}: {exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}");
                }
            }
        }
        finally
        {
            PreferencesState.CreateUsernameSubfolders = originalUsernameSubfolders;

            try
            {
                DeleteDirectoryContents(fileSystem.DocumentsPath);
                DeleteDirectoryContents(fileSystem.IncompletePath);
            }
            catch (Exception exception)
            {
                failures.Add($"suite-cleanup: {exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}");
            }
        }

        string marker = failures.Count == 0 ? SuccessMarker : FailureMarker;
        string report = BuildReport(marker, passed, failures);
        File.WriteAllText(resultPath, report, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine(report);
        Console.Out.Flush();
        return failures.Count == 0 ? 0 : 1;
    }

    private static void AppendAndReopenPreservesPosition(IosFileSystemService fileSystem)
    {
        byte[] first = [0x01, 0x02, 0x03, 0x04];
        byte[] second = [0x05, 0x06, 0x07];
        fileSystem.GetOrCreateIncompleteLocation(
            "position-user",
            @"album\position.bin",
            depth: 1,
            out string partialPath,
            out _,
            out long initialLength);
        Ensure(initialLength == 0, $"Expected a new partial length of 0, got {initialLength}.");

        using (Stream firstStream = fileSystem.OpenIncompleteStream(partialPath, initialLength))
        {
            Ensure(firstStream.Position == 0, $"Initial stream position was {firstStream.Position}.");
            firstStream.Write(first);
            Ensure(firstStream.Position == first.Length, "The first write did not advance the stream position.");
            firstStream.Flush();
        }

        fileSystem.GetOrCreateIncompleteLocation(
            "position-user",
            @"album\position.bin",
            depth: 1,
            out string reopenedPath,
            out _,
            out long reopenedLength);
        Ensure(reopenedPath == partialPath, "Reopening resolved a different partial path.");
        Ensure(reopenedLength == first.Length, $"Reopened length was {reopenedLength}, expected {first.Length}.");

        using (Stream reopened = fileSystem.OpenIncompleteStream(reopenedPath, reopenedLength))
        {
            Ensure(reopened.Position == first.Length, $"Reopened position was {reopened.Position}.");
            reopened.Write(second);
            Ensure(reopened.Position == first.Length + second.Length, "The appended write position was incorrect.");
            reopened.Flush();
        }

        Ensure(
            File.ReadAllBytes(partialPath).SequenceEqual(first.Concat(second)),
            "Reopening and appending did not preserve the complete byte sequence.");
        Ensure(fileSystem.DeleteIncompleteFile(partialPath), "The append test partial could not be cleaned up.");
    }

    private static void ResumeUsesActualPartialLength(IosFileSystemService fileSystem)
    {
        byte[] partialBytes = Enumerable.Range(0, 257).Select(value => (byte)value).ToArray();
        byte[] resumedBytes = [0xFE, 0xED, 0xFA, 0xCE];
        fileSystem.GetOrCreateIncompleteLocation(
            "resume-user",
            @"nested\resume.bin",
            depth: 1,
            out string partialPath,
            out _,
            out _);
        File.WriteAllBytes(partialPath, partialBytes);

        fileSystem.GetOrCreateIncompleteLocation(
            "resume-user",
            @"nested\resume.bin",
            depth: 1,
            out string resumedPath,
            out _,
            out long actualPartialLength);
        Ensure(actualPartialLength == partialBytes.Length, "The service did not report the partial file's actual length.");

        using (Stream stream = fileSystem.OpenIncompleteStream(resumedPath, actualPartialLength))
        {
            Ensure(stream.Position == actualPartialLength, "The resumed stream did not start at the actual partial length.");
            stream.Write(resumedBytes);
            stream.Flush();
        }

        Ensure(
            File.ReadAllBytes(partialPath).SequenceEqual(partialBytes.Concat(resumedBytes)),
            "Resume overwrote or skipped existing partial bytes.");
        Ensure(fileSystem.DeleteIncompleteFile(partialPath), "The resume test partial could not be cleaned up.");
    }

    private static void FinalizationMovesExactBytes(IosFileSystemService fileSystem)
    {
        byte[] expected = Encoding.UTF8.GetBytes("AnimaSeek iOS finalize semantics\n");
        fileSystem.GetOrCreateIncompleteLocation(
            "final-user",
            @"release\final.bin",
            depth: 1,
            out string partialPath,
            out string partialParent,
            out _);
        File.WriteAllBytes(partialPath, expected);

        string finalPath = fileSystem.SaveToFile(
            @"release\final.bin",
            "final-user",
            ArraySegment<byte>.Empty,
            partialPath,
            partialParent,
            memoryMode: false,
            depth: 1,
            noSubFolder: false,
            out string finalUri);
        fileSystem.OnFileFinalized(finalPath);

        Ensure(!File.Exists(partialPath), "Finalization left the source partial file behind.");
        Ensure(File.Exists(finalPath), "Finalization did not create the destination file.");
        Ensure(File.ReadAllBytes(finalPath).SequenceEqual(expected), "Finalization changed the transferred bytes.");
        Ensure(finalUri.StartsWith("file://", StringComparison.Ordinal), $"Unexpected final URI: {finalUri}");
        Ensure(IsContained(fileSystem.DocumentsPath, finalPath), "The finalized file escaped Documents.");
        PendingDownloadFinalization prepared = new IosDownloadFinalizationJournal(fileSystem.ApplicationSupportPath)
            .Read()
            .Single(entry => entry.Username == "final-user" && entry.Filename == @"release\final.bin");
        DownloadFinalizationFileSnapshot snapshot = fileSystem.InspectFinalization(prepared);
        Ensure(!snapshot.SourceExists, "The prepared finalization still reported its moved source.");
        Ensure(snapshot.TargetExists, "The prepared finalization did not report its exact destination.");
        Ensure(snapshot.TargetLength == expected.Length, "The prepared destination length was not exact.");
        Ensure(
            IosDownloadFinalizationRecoveryPolicy.Classify(
                managerSucceeded: false,
                snapshot.SourceExists,
                snapshot.TargetExists,
                snapshot.TargetLength,
                prepared.ActualLength) == DownloadFinalizationRecoveryAction.CommitFinalFile,
            "A post-move restart did not recognize the exact finalized bytes.");
        fileSystem.FinalizationJournal.Complete(prepared.Username, prepared.Filename);
        File.Delete(finalPath);
        DeleteEmptyParents(fileSystem.DocumentsPath, Path.GetDirectoryName(finalPath));
    }

    private static void PrivatePartialsUseCompleteTransferIdentity(IosFileSystemService fileSystem)
    {
        const string sharedRemotePath = @"album\shared.bin";
        fileSystem.GetOrCreateIncompleteLocation(
            "alice",
            sharedRemotePath,
            depth: 1,
            out string alicePath,
            out _,
            out _);
        fileSystem.GetOrCreateIncompleteLocation(
            "bob",
            sharedRemotePath,
            depth: 1,
            out string bobPath,
            out _,
            out _);
        fileSystem.GetOrCreateIncompleteLocation(
            "alice",
            @"first\collapsed\same.bin",
            depth: 1,
            out string firstDeepPath,
            out _,
            out _);
        fileSystem.GetOrCreateIncompleteLocation(
            "alice",
            @"second\collapsed\same.bin",
            depth: 1,
            out string secondDeepPath,
            out _,
            out _);
        fileSystem.GetOrCreateIncompleteLocation(
            "alice",
            @"sanitize\same:name.bin",
            depth: 1,
            out string colonPath,
            out _,
            out _);
        fileSystem.GetOrCreateIncompleteLocation(
            "alice",
            @"sanitize\samename.bin",
            depth: 1,
            out string noColonPath,
            out _,
            out _);

        Ensure(alicePath != bobPath, "Two users resolved to the same private partial path.");
        Ensure(firstDeepPath != secondDeepPath, "Depth-truncated remote paths resolved to the same private partial.");
        Ensure(colonPath != noColonPath, "Sanitization-colliding remote paths resolved to the same private partial.");
        foreach (string path in new[] { alicePath, bobPath, firstDeepPath, secondDeepPath, colonPath, noColonPath })
        {
            Ensure(IsContained(fileSystem.IncompletePath, path), "An identity-keyed partial escaped Incomplete.");
        }

        File.WriteAllBytes(alicePath, [0xA1]);
        File.WriteAllBytes(bobPath, [0xB2, 0xB3]);
        fileSystem.GetOrCreateIncompleteLocation(
            "alice",
            sharedRemotePath,
            depth: 1,
            out string reopenedAlicePath,
            out _,
            out long aliceLength);
        fileSystem.GetOrCreateIncompleteLocation(
            "bob",
            sharedRemotePath,
            depth: 1,
            out string reopenedBobPath,
            out _,
            out long bobLength);
        Ensure(reopenedAlicePath == alicePath && aliceLength == 1, "Alice's deterministic partial did not reopen.");
        Ensure(reopenedBobPath == bobPath && bobLength == 2, "Bob's deterministic partial did not reopen.");
        Ensure(File.ReadAllBytes(alicePath).SequenceEqual(new byte[] { 0xA1 }), "Alice's partial bytes changed.");
        Ensure(File.ReadAllBytes(bobPath).SequenceEqual(new byte[] { 0xB2, 0xB3 }), "Bob's partial bytes changed.");

        string ambiguousLegacyPath = Path.Combine(fileSystem.IncompletePath, "legacy", "ambiguous.bin.part");
        Directory.CreateDirectory(Path.GetDirectoryName(ambiguousLegacyPath)!);
        File.WriteAllBytes(ambiguousLegacyPath, [0xDE, 0xAD]);
        fileSystem.GetOrCreateIncompleteLocation(
            "legacy-user",
            @"legacy\ambiguous.bin",
            depth: 1,
            out string safeRestartPath,
            out _,
            out long safeRestartLength);
        Ensure(safeRestartPath != ambiguousLegacyPath, "The ambiguous legacy partial was reused as canonical state.");
        Ensure(safeRestartLength == 0, "Ambiguous legacy bytes were assigned to a new transfer identity.");
        Ensure(!File.Exists(ambiguousLegacyPath), "The ambiguous legacy partial was not retired.");

        foreach (string path in new[]
                 {
                     alicePath,
                     bobPath,
                     firstDeepPath,
                     secondDeepPath,
                     colonPath,
                     noColonPath,
                     safeRestartPath,
                 }.Distinct(StringComparer.Ordinal))
        {
            _ = fileSystem.DeleteIncompleteFile(path);
        }
    }

    private static void ProductionFinalizationCollisionAndTraversalStayContained(IosFileSystemService fileSystem)
    {
        const string remotePath = @"collision\song.bin";
        byte[] firstBytes = [0x01, 0x02];
        byte[] secondBytes = [0x03, 0x04, 0x05];
        byte[] memoryBytes = [0x06];
        fileSystem.GetOrCreateIncompleteLocation(
            "alice",
            remotePath,
            depth: 1,
            out string firstPartial,
            out string firstParent,
            out _);
        fileSystem.GetOrCreateIncompleteLocation(
            "bob",
            remotePath,
            depth: 1,
            out string secondPartial,
            out string secondParent,
            out _);
        File.WriteAllBytes(firstPartial, firstBytes);
        File.WriteAllBytes(secondPartial, secondBytes);

        string firstPath = fileSystem.SaveToFile(
            remotePath,
            "alice",
            ArraySegment<byte>.Empty,
            firstPartial,
            firstParent,
            memoryMode: false,
            depth: 1,
            noSubFolder: false,
            out _);
        string secondPath = fileSystem.SaveToFile(
            remotePath,
            "bob",
            ArraySegment<byte>.Empty,
            secondPartial,
            secondParent,
            memoryMode: false,
            depth: 1,
            noSubFolder: false,
            out _);
        string memoryPath = fileSystem.SaveToFile(
            remotePath,
            "carol",
            new ArraySegment<byte>(memoryBytes),
            null!,
            null!,
            memoryMode: true,
            depth: 1,
            noSubFolder: false,
            out _);

        Ensure(IsContained(fileSystem.DocumentsPath, firstPath), "The first finalized file escaped Documents.");
        Ensure(IsContained(fileSystem.DocumentsPath, secondPath), "The second finalized file escaped Documents.");
        Ensure(IsContained(fileSystem.DocumentsPath, memoryPath), "The memory-backed finalized file escaped Documents.");
        Ensure(Path.GetFileName(firstPath) == "song.bin", $"Unexpected first destination: {firstPath}");
        Ensure(Path.GetFileName(secondPath) == "song (2).bin", $"Unexpected second destination: {secondPath}");
        Ensure(Path.GetFileName(memoryPath) == "song (3).bin", $"Unexpected third destination: {memoryPath}");
        Ensure(File.ReadAllBytes(firstPath).SequenceEqual(firstBytes), "The first finalized bytes were overwritten.");
        Ensure(File.ReadAllBytes(secondPath).SequenceEqual(secondBytes), "The colliding finalized bytes were changed.");
        Ensure(File.ReadAllBytes(memoryPath).SequenceEqual(memoryBytes), "The memory-backed collision bytes were changed.");

        string incompleteEscape = Path.Combine(fileSystem.IncompletePath, "..", "escape.part");
        ExpectUnauthorized(
            () => fileSystem.OpenIncompleteStream(incompleteEscape, 0),
            "Opening a traversal path outside Incomplete was accepted.");
        ExpectUnauthorized(
            () => fileSystem.ResolveDocumentsRelativePath(Path.Combine("..", "escape.bin")),
            "Resolving a parent-relative Documents path was accepted.");
        ExpectUnauthorized(
            () => fileSystem.DeleteIncompleteFile(incompleteEscape),
            "Deleting a traversal path outside Incomplete was accepted.");

        foreach (string path in new[] { firstPath, secondPath, memoryPath })
        {
            File.Delete(path);
        }

        DeleteEmptyParents(fileSystem.DocumentsPath, Path.GetDirectoryName(firstPath));
        fileSystem.FinalizationJournal.Complete("alice", remotePath);
        fileSystem.FinalizationJournal.Complete("bob", remotePath);
    }

    private static void UserDefaultsRoundTripAndFlushFailure(IosFileSystemService fileSystem)
    {
        string suiteName = $"com.animaseek.filesystemtests.{Guid.NewGuid():N}";
        using var defaults = new NSUserDefaults(suiteName, NSUserDefaultsType.SuiteName);
        defaults.RemovePersistentDomain(suiteName);
        var store = new UserDefaultsKeyValueStore(defaults);
        store.PutBoolean("bool", true);
        store.PutInt("int", 793);
        store.PutLong("long", (long)int.MaxValue + 42);
        store.PutString("string", "AnimaSeek");
        store.PutStringSet("set", new[] { "alpha", "beta", "alpha" });
        store.Flush();

        using var reopenedDefaults = new NSUserDefaults(suiteName, NSUserDefaultsType.SuiteName);
        var reopened = new UserDefaultsKeyValueStore(reopenedDefaults);
        Ensure(reopened.GetBoolean("bool", false), "Boolean defaults value did not round-trip.");
        Ensure(reopened.GetInt("int", 0) == 793, "Integer defaults value did not round-trip.");
        Ensure(reopened.GetLong("long", 0) == (long)int.MaxValue + 42, "Long defaults value did not round-trip.");
        Ensure(reopened.GetString("string") == "AnimaSeek", "String defaults value did not round-trip.");
        Ensure(
            reopened.GetStringSet("set")?.Order(StringComparer.Ordinal).SequenceEqual(new[] { "alpha", "beta" }) == true,
            "String-set defaults value did not round-trip through NSUserDefaults object APIs.");
        Ensure(!reopened.GetBoolean("missing-bool", false), "Missing Boolean default was ignored.");
        Ensure(reopened.GetInt("missing-int", 17) == 17, "Missing integer default was ignored.");
        Ensure(reopened.GetLong("missing-long", 18) == 18, "Missing long default was ignored.");

        var failedFlush = new UserDefaultsKeyValueStore(reopenedDefaults, static () => false);
        try
        {
            failedFlush.Flush();
            throw new InvalidOperationException("A failed NSUserDefaults synchronization was reported as durable.");
        }
        catch (IOException)
        {
            // Expected: critical callers must retain their recovery state when the platform rejects a flush.
        }
        finally
        {
            reopenedDefaults.RemovePersistentDomain(suiteName);
            _ = reopenedDefaults.Synchronize();
        }
    }

    private static void CleanupRemovesPartialAndEmptyParents(IosFileSystemService fileSystem)
    {
        fileSystem.GetOrCreateIncompleteLocation(
            "cleanup-user",
            @"one\two\cleanup.bin",
            depth: 2,
            out string partialPath,
            out string partialParent,
            out _);
        File.WriteAllBytes(partialPath, [0xCA, 0xFE]);

        Ensure(fileSystem.DeleteIncompleteFile(partialPath), "DeleteIncompleteFile did not delete an existing file.");
        Ensure(!File.Exists(partialPath), "The partial file still exists after cleanup.");
        Ensure(!Directory.Exists(partialParent), "Cleanup did not remove the empty nested parent.");
        Ensure(!fileSystem.DeleteIncompleteFile(partialPath), "Deleting an already removed partial should return false.");
        Ensure(Directory.Exists(fileSystem.IncompletePath), "Cleanup removed the incomplete root directory.");
    }

    private static string BuildReport(string marker, IEnumerable<string> passed, IEnumerable<string> failures)
    {
        var lines = new List<string> { marker };
        lines.AddRange(passed.Select(name => $"PASS {name}"));
        lines.AddRange(failures.Select(failure => $"FAIL {failure}"));
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static void ExpectUnauthorized(Action action, string message)
    {
        try
        {
            action();
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static bool IsContained(string root, string path)
    {
        string canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(canonicalRoot, StringComparison.Ordinal);
    }

    private static void DeleteEmptyParents(string root, string? directory)
    {
        string canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var current = directory is null ? null : new DirectoryInfo(directory);
        while (current is not null &&
               !string.Equals(current.FullName, canonicalRoot, StringComparison.Ordinal) &&
               !current.EnumerateFileSystemInfos().Any())
        {
            DirectoryInfo? parent = current.Parent;
            current.Delete();
            current = parent;
        }
    }

    private static void DeleteDirectoryContents(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(directory))
        {
            File.Delete(file);
        }

        foreach (string childDirectory in Directory.EnumerateDirectories(directory))
        {
            Directory.Delete(childDirectory, recursive: true);
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
