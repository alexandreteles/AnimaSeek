using System;
using System.IO;

namespace Seeker.Services
{
    /// <summary>Provides platform-specific paths and file operations used by portable downloads.</summary>
    public interface IFileSystemService
    {
        /// <summary>Resolves or creates a partial-download location and reports its current length.</summary>
        void GetOrCreateIncompleteLocation(string username, string fullfilename, int depth,
            out string incompleteUri, out string parentUri, out long partialLength);

        /// <summary>Opens a partial file positioned for appending at <paramref name="partialLength"/>.</summary>
        Stream OpenIncompleteStream(string incompleteUri, long partialLength);

        /// <summary>Moves or writes completed bytes into the platform's final download location.</summary>
        string SaveToFile(string fullfilename, string username, ArraySegment<byte> bytes,
            string uriOfIncomplete, string parentUriOfIncomplete,
            bool memoryMode, int depth, bool noSubFolder, out string finalUri);

        /// <summary>
        /// Lets the platform publish or otherwise react to a completed file at <paramref name="path"/>.
        /// </summary>
        void OnFileFinalized(string path);
    }
}
