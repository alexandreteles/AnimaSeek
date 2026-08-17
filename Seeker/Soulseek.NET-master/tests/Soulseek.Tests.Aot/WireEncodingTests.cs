// <copyright file="WireEncodingTests.cs" company="AnimaSeek contributors">
//     Copyright (c) 2026 AnimaSeek contributors.
//
//     This program is free software: you can redistribute it and/or modify
//     it under the terms of the GNU General Public License as published by
//     the Free Software Foundation, version 3.
//
//     This program is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//
//     You should have received a copy of the GNU General Public License
//     along with this program.  If not, see https://www.gnu.org/licenses/.
//
//     This program is distributed with Additional Terms pursuant to Section 7
//     of the GPLv3.  See the LICENSE file in the root directory of this
//     project for the complete terms and conditions.
//
//     SPDX-FileCopyrightText: 2026 AnimaSeek contributors
//     SPDX-License-Identifier: GPL-3.0-only
// </copyright>

namespace AnimaSeek.ProtocolComponent.AotTests;

using System;
using System.Linq;
using NUnit.Framework;
using Soulseek;
using Soulseek.Messaging;

/// <summary>
///     Regression pins for the fork's Latin-1 wire-encoding patches to <see cref="MessageBuilder"/> and
///     <see cref="MessageReader{T}"/>: strings are written as UTF-8 by default (with optional Latin-1
///     re-encoding of the folder and/or file portions for legacy SoulseekNS peers, falling back to UTF-8
///     for the whole value when Latin-1 cannot represent it), and reads attempt strict UTF-8 first,
///     falling back to Latin-1 for byte sequences that are not valid UTF-8.
/// </summary>
[TestFixture]
public sealed class WireEncodingTests
{
    // a full message is [4-byte length][4-byte peer code][payload]; the string payload starts at offset 8.
    private const int PayloadOffset = 8;

    private static MessageBuilder CreateBuilder() => new MessageBuilder().WriteCode(MessageCode.Peer.InfoRequest);

    private static byte[] GetStringPayload(MessageBuilder builder)
    {
        // returns [4-byte string length][string bytes]
        return builder.Build().Skip(PayloadOffset).ToArray();
    }

    private static MessageReader<MessageCode.Peer> CreateReader(MessageBuilder builder)
    {
        return new MessageReader<MessageCode.Peer>(builder.Build());
    }

    [Test]
    public void WriteString_Ascii_RoundTrips_Through_ReadString()
    {
        var builder = CreateBuilder().WriteString("hello world.mp3");
        var payload = GetStringPayload(builder);

        Assert.That(BitConverter.ToInt32(payload, 0), Is.EqualTo(15));

        var value = CreateReader(builder).ReadStringAndNoteEncoding(out bool isDecodedViaLatin1);

        Assert.That(value, Is.EqualTo("hello world.mp3"));
        Assert.That(isDecodedViaLatin1, Is.False);
    }

    [Test]
    public void WriteString_Empty_String_Writes_Zero_Length_And_RoundTrips()
    {
        var builder = CreateBuilder().WriteString(string.Empty);
        var payload = GetStringPayload(builder);

        Assert.That(payload, Is.EqualTo(new byte[] { 0, 0, 0, 0 }));
        Assert.That(CreateReader(builder).ReadString(), Is.EqualTo(string.Empty));
    }

    [Test]
    public void WriteString_Default_Encodes_Latin1_High_Char_As_Utf8_And_RoundTrips()
    {
        // 'ü' (U+00FC) is UTF-8 0xC3 0xBC; the default write path is UTF-8, so the length prefix counts 2 bytes.
        var builder = CreateBuilder().WriteString("ü");
        var payload = GetStringPayload(builder);

        Assert.That(BitConverter.ToInt32(payload, 0), Is.EqualTo(2));
        Assert.That(payload.Skip(4), Is.EqualTo(new byte[] { 0xC3, 0xBC }));

        var value = CreateReader(builder).ReadStringAndNoteEncoding(out bool isDecodedViaLatin1);

        Assert.That(value, Is.EqualTo("ü"));
        Assert.That(isDecodedViaLatin1, Is.False);
    }

    [Test]
    public void WriteString_Utf8_MultiByte_RoundTrips_With_Correct_Length_Prefix()
    {
        // '€' (U+20AC) and '漢' (U+6F22) are three UTF-8 bytes each.
        var builder = CreateBuilder().WriteString("€漢");
        var payload = GetStringPayload(builder);

        Assert.That(BitConverter.ToInt32(payload, 0), Is.EqualTo(6));
        Assert.That(payload.Skip(4), Is.EqualTo(new byte[] { 0xE2, 0x82, 0xAC, 0xE6, 0xBC, 0xA2 }));

        var (value, encoding) = CreateReader(builder).ReadStringAndEncoding();

        Assert.That(value, Is.EqualTo("€漢"));
        Assert.That((string)encoding, Is.EqualTo("UTF-8"));
    }

    [Test]
    public void WriteString_Latin1Echo_Encodes_High_Chars_As_Single_Latin1_Bytes()
    {
        // when both Latin-1 flags are set (the echo path for strings originally decoded via Latin-1),
        // the whole value is re-encoded as ISO-8859-1 so the peer receives its original byte sequence.
        var builder = CreateBuilder().WriteString("Fünf.mp3", attemptLatin1File: true, attemptLatin1Folder: true);
        var payload = GetStringPayload(builder);

        Assert.That(BitConverter.ToInt32(payload, 0), Is.EqualTo(8));
        Assert.That(payload.Skip(4), Is.EqualTo(new byte[] { 0x46, 0xFC, 0x6E, 0x66, 0x2E, 0x6D, 0x70, 0x33 }));

        var value = CreateReader(builder).ReadStringAndNoteEncoding(out bool isDecodedViaLatin1);

        Assert.That(value, Is.EqualTo("Fünf.mp3"));
        Assert.That(isDecodedViaLatin1, Is.True);
    }

    [Test]
    public void WriteString_Latin1Echo_Falls_Back_To_Utf8_When_Not_Latin1_Representable()
    {
        // '漢' cannot be encoded as ISO-8859-1; the write path falls back to UTF-8 for the whole value.
        var builder = CreateBuilder().WriteString("漢字.mp3", attemptLatin1File: true, attemptLatin1Folder: true);
        var payload = GetStringPayload(builder);

        var expected = System.Text.Encoding.UTF8.GetBytes("漢字.mp3");
        Assert.That(BitConverter.ToInt32(payload, 0), Is.EqualTo(expected.Length));
        Assert.That(payload.Skip(4), Is.EqualTo(expected));

        var value = CreateReader(builder).ReadStringAndNoteEncoding(out bool isDecodedViaLatin1);

        Assert.That(value, Is.EqualTo("漢字.mp3"));
        Assert.That(isDecodedViaLatin1, Is.False);
    }

    [Test]
    public void WriteString_Latin1Folder_Utf8File_Splits_On_Last_Backslash()
    {
        // folder portion (through the trailing backslash) is Latin-1; file portion is UTF-8.
        var builder = CreateBuilder().WriteString("földer\\fïle.mp3", attemptLatin1File: false, attemptLatin1Folder: true);
        var payload = GetStringPayload(builder);

        var expected = new byte[]
        {
            0x66, 0xF6, 0x6C, 0x64, 0x65, 0x72, 0x5C, // "földer\" as ISO-8859-1
            0x66, 0xC3, 0xAF, 0x6C, 0x65, 0x2E, 0x6D, 0x70, 0x33, // "fïle.mp3" as UTF-8
        };

        Assert.That(BitConverter.ToInt32(payload, 0), Is.EqualTo(expected.Length));
        Assert.That(payload.Skip(4), Is.EqualTo(expected));
    }

    [Test]
    public void WriteString_Utf8Folder_Latin1File_Splits_On_Last_Backslash()
    {
        // folder portion is UTF-8; file portion is Latin-1.
        var builder = CreateBuilder().WriteString("földer\\fïle.mp3", attemptLatin1File: true, attemptLatin1Folder: false);
        var payload = GetStringPayload(builder);

        var expected = new byte[]
        {
            0x66, 0xC3, 0xB6, 0x6C, 0x64, 0x65, 0x72, 0x5C, // "földer\" as UTF-8
            0x66, 0xEF, 0x6C, 0x65, 0x2E, 0x6D, 0x70, 0x33, // "fïle.mp3" as ISO-8859-1
        };

        Assert.That(BitConverter.ToInt32(payload, 0), Is.EqualTo(expected.Length));
        Assert.That(payload.Skip(4), Is.EqualTo(expected));
    }

    [Test]
    public void WriteString_Split_Path_Falls_Back_To_Utf8_For_Whole_Value_When_Latin1_Portion_Fails()
    {
        // the folder portion '漢\' is not Latin-1 representable, so the entire value is written as UTF-8.
        var builder = CreateBuilder().WriteString("漢\\file.mp3", attemptLatin1File: false, attemptLatin1Folder: true);
        var payload = GetStringPayload(builder);

        var expected = System.Text.Encoding.UTF8.GetBytes("漢\\file.mp3");
        Assert.That(BitConverter.ToInt32(payload, 0), Is.EqualTo(expected.Length));
        Assert.That(payload.Skip(4), Is.EqualTo(expected));
    }

    [Test]
    public void WriteString_Explicit_Latin1_Encoding_Writes_Single_Bytes()
    {
        var builder = CreateBuilder().WriteString("Fünf", encoding: CharacterEncoding.ISO88591);
        var payload = GetStringPayload(builder);

        Assert.That(BitConverter.ToInt32(payload, 0), Is.EqualTo(4));
        Assert.That(payload.Skip(4), Is.EqualTo(new byte[] { 0x46, 0xFC, 0x6E, 0x66 }));
    }

    [Test]
    public void WriteString_Explicit_Latin1_Encoding_Falls_Back_To_Utf8_When_Unrepresentable()
    {
        // '€' is not in ISO-8859-1; instead of writing replacement characters, the builder fails 'up' to UTF-8.
        var builder = CreateBuilder().WriteString("€", encoding: CharacterEncoding.ISO88591);
        var payload = GetStringPayload(builder);

        Assert.That(BitConverter.ToInt32(payload, 0), Is.EqualTo(3));
        Assert.That(payload.Skip(4), Is.EqualTo(new byte[] { 0xE2, 0x82, 0xAC }));
    }

    [Test]
    public void ReadString_Invalid_Utf8_Bytes_Decode_Via_Latin1_Fallback()
    {
        // 0xFC 0xE9 is not valid UTF-8 but is valid Latin-1 ("üé"), as sent by legacy SoulseekNS peers.
        var raw = new byte[] { 0xFC, 0xE9 };
        var builder = CreateBuilder()
            .WriteBytes(BitConverter.GetBytes(raw.Length))
            .WriteBytes(raw);

        var (value, encoding) = CreateReader(builder).ReadStringAndEncoding();

        Assert.That(value, Is.EqualTo("üé"));
        Assert.That((string)encoding, Is.EqualTo("ISO-8859-1"));
    }

    [Test]
    public void ReadStringAndNoteEncoding_Flags_Latin1_Fallback()
    {
        var raw = new byte[] { 0x61, 0xE9, 0x62 }; // "aéb" in Latin-1; invalid as UTF-8
        var builder = CreateBuilder()
            .WriteBytes(BitConverter.GetBytes(raw.Length))
            .WriteBytes(raw);

        var value = CreateReader(builder).ReadStringAndNoteEncoding(out bool isDecodedViaLatin1);

        Assert.That(value, Is.EqualTo("aéb"));
        Assert.That(isDecodedViaLatin1, Is.True);
    }

    [Test]
    public void ReadString_Valid_Utf8_Reports_Utf8_Encoding()
    {
        var builder = CreateBuilder().WriteString("crème.flac");

        var (value, encoding) = CreateReader(builder).ReadStringAndEncoding();

        Assert.That(value, Is.EqualTo("crème.flac"));
        Assert.That((string)encoding, Is.EqualTo("UTF-8"));
    }
}
