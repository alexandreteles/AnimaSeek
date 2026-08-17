// <copyright file="WaiterAotTests.cs" company="AnimaSeek contributors">
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
using System.Threading.Tasks;
using NUnit.Framework;
using Soulseek;

/// <summary>
///     Covers the statically dispatched waiter operations used under full AOT.
/// </summary>
[TestFixture]
public sealed class WaiterAotTests
{
    /// <summary>
    ///     Verifies that a result is dispatched through the non-generic completion abstraction.
    /// </summary>
    [Test]
    public async Task Complete_Dispatches_Typed_Result_Without_Dynamic_Binding()
    {
        using var waiter = new Waiter();
        var key = new WaitKey("typed-completion");
        Task<int> wait = waiter.Wait<int>(key);

        waiter.Complete(key, 42);

        Assert.That(await wait, Is.EqualTo(42));
        Assert.That(waiter.HasWait(key), Is.False);
    }

    /// <summary>
    ///     Verifies that a mismatched result reports a useful error and still cleans up the pending wait.
    /// </summary>
    [Test]
    public void Complete_Rejects_Mismatched_Result_Type_And_Cleans_Up()
    {
        using var waiter = new Waiter();
        var key = new WaitKey("mismatched-completion");
        _ = waiter.Wait<string>(key);

        SoulseekClientException exception = Assert.Throws<SoulseekClientException>(() => waiter.Complete(key, 42))!;

        Assert.That(exception.InnerException, Is.TypeOf<InvalidCastException>());
        Assert.That(waiter.HasWait(key), Is.False);
    }

    /// <summary>
    ///     Verifies that the non-generic Complete (an object-typed null) against a typed wait throws,
    ///     matching upstream semantics, instead of silently completing the wait with a null result.
    /// </summary>
    [Test]
    public void Complete_NonGeneric_Rejects_Typed_Wait_And_Cleans_Up()
    {
        using var waiter = new Waiter();
        var key = new WaitKey("non-generic-vs-typed");
        _ = waiter.Wait<string>(key);

        SoulseekClientException exception = Assert.Throws<SoulseekClientException>(() => waiter.Complete(key))!;

        Assert.That(exception.InnerException, Is.TypeOf<InvalidCastException>());
        Assert.That(waiter.HasWait(key), Is.False);
    }

    /// <summary>
    ///     Verifies that the non-generic Complete against a non-generic (object-typed) wait succeeds.
    /// </summary>
    [Test]
    public async Task Complete_NonGeneric_Succeeds_Against_NonGeneric_Wait()
    {
        using var waiter = new Waiter();
        var key = new WaitKey("non-generic-vs-non-generic");
        Task wait = waiter.Wait(key);

        waiter.Complete(key);

        await wait;
        Assert.That(wait.IsCompletedSuccessfully, Is.True);
        Assert.That(waiter.HasWait(key), Is.False);
    }

    /// <summary>
    ///     Verifies that a null result completes a typed wait when the declared completion type matches the wait type.
    /// </summary>
    [Test]
    public async Task Complete_Null_Succeeds_When_Declared_Type_Matches()
    {
        using var waiter = new Waiter();
        var key = new WaitKey("typed-null-completion");
        Task<string> wait = waiter.Wait<string>(key);

        waiter.Complete<string>(key, null!);

        Assert.That(await wait, Is.Null);
        Assert.That(waiter.HasWait(key), Is.False);
    }

    /// <summary>
    ///     Verifies that completion requires an exact declared-type match; a completion declared with a derived
    ///     type does not satisfy a wait declared with its base type, matching upstream's invariant
    ///     TaskCompletionSource cast semantics.
    /// </summary>
    [Test]
    public void Complete_Rejects_Derived_Declared_Type_Against_Base_Wait()
    {
        using var waiter = new Waiter();
        var key = new WaitKey("derived-declared-completion");
        _ = waiter.Wait<BaseResult>(key);

        SoulseekClientException exception = Assert.Throws<SoulseekClientException>(
            () => waiter.Complete(key, new DerivedResult()))!;

        Assert.That(exception.InnerException, Is.TypeOf<InvalidCastException>());
        Assert.That(waiter.HasWait(key), Is.False);
    }

    /// <summary>
    ///     Verifies that a derived instance completes a wait when the declared completion type matches the wait type.
    /// </summary>
    [Test]
    public async Task Complete_Accepts_Derived_Instance_With_Matching_Declared_Type()
    {
        using var waiter = new Waiter();
        var key = new WaitKey("derived-instance-completion");
        Task<BaseResult> wait = waiter.Wait<BaseResult>(key);

        waiter.Complete<BaseResult>(key, new DerivedResult());

        Assert.That(await wait, Is.TypeOf<DerivedResult>());
        Assert.That(waiter.HasWait(key), Is.False);
    }

    private class BaseResult
    {
    }

    private sealed class DerivedResult : BaseResult
    {
    }
}
