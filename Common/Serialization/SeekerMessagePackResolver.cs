using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;
using Soulseek;

namespace Seeker.Serialization
{
    /// <summary>
    /// Contains the source-generated formatters for MessagePack objects declared in Common.
    /// </summary>
    [GeneratedMessagePackResolver]
    internal partial class SeekerGeneratedMessagePackResolver
    {
    }

    /// <summary>
    /// Resolves every MessagePack type persisted by Seeker without reflection or runtime code generation.
    /// </summary>
    /// <remarks>
    /// The closed generic formatter registrations are intentional. MessagePack's dynamic generic resolver
    /// creates these formatters through reflection, which is not safe under iOS full AOT. Keeping the list
    /// explicit also makes additions to the on-disk serialization surface visible during review.
    /// </remarks>
    [CompositeResolver(
        typeof(FileItemFormatter),
        typeof(FileAttributeFormatter),
        typeof(BrowseResponseFormatter),
        typeof(DirectoryItemFormatter),
        typeof(SearchResponseFormatter),
        typeof(UserListItemFormatter),
        typeof(UserDataFormatter),
        typeof(UserStatusFormatter),
        typeof(UserInfoFormatter),
        typeof(MessageFormatter),
        typeof(ListFormatter<Directory>),
        typeof(ListFormatter<SearchResponse>),
        typeof(ListFormatter<UserListItem>),
        typeof(ListFormatter<Message>),
        typeof(ListFormatter<int>),
        typeof(ListFormatter<Tuple<string, string>>),
        typeof(DictionaryFormatter<int, string>),
        typeof(DictionaryFormatter<string, List<int>>),
        typeof(DictionaryFormatter<string, Tuple<long, string, Tuple<int, int, int, int>, bool, bool>>),
        typeof(ConcurrentDictionaryFormatter<string, ConcurrentDictionary<string, List<Message>>>),
        typeof(ConcurrentDictionaryFormatter<string, List<Message>>),
        typeof(TupleFormatter<string, string>),
        typeof(TupleFormatter<int, int, int, int>),
        typeof(TupleFormatter<long, string, Tuple<int, int, int, int>, bool, bool>),
        typeof(BuiltinResolver),
        typeof(SeekerGeneratedMessagePackResolver))]
    internal partial class SeekerMessagePackResolver
    {
    }
}
