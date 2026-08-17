// <copyright file="IWaitCompletionSource.cs" company="AnimaSeek contributors">
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

namespace Soulseek
{
    using System;

    /// <summary>
    ///     Provides non-generic completion operations for a pending wait.
    /// </summary>
    internal interface IWaitCompletionSource
    {
        /// <summary>
        ///     Attempts to transition the wait task to the canceled state.
        /// </summary>
        /// <returns><see langword="true"/> if the transition succeeded; otherwise, <see langword="false"/>.</returns>
        bool TrySetCanceled();

        /// <summary>
        ///     Attempts to transition the wait task to the faulted state.
        /// </summary>
        /// <param name="exception">The exception with which to fault the task.</param>
        /// <returns><see langword="true"/> if the transition succeeded; otherwise, <see langword="false"/>.</returns>
        bool TrySetException(Exception exception);

        /// <summary>
        ///     Attempts to transition the wait task to the completed state.
        /// </summary>
        /// <param name="result">The result whose runtime type must match the wait result type.</param>
        /// <returns><see langword="true"/> if the transition succeeded; otherwise, <see langword="false"/>.</returns>
        /// <exception cref="InvalidCastException">Thrown when <paramref name="result"/> is incompatible with the wait result type.</exception>
        bool TrySetResult(object result);
    }
}
