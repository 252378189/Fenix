﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace DotNetty.Transport.Channels
{
    using System;
    using DotNetty.Buffers;

    public interface IChannelConfiguration
    {
        /// <summary>Return the value of the given <see cref="ChannelOption{T}"/>.</summary>
        T GetOption<T>(ChannelOption<T> option);

        /// <summary>Sets a configuration property with the specified name and value.</summary>
        /// <returns><c>true</c> if and only if the property has been set</returns>
        bool SetOption(ChannelOption option, object value);

        /// <summary>Sets a configuration property with the specified name and value.</summary>
        /// <returns><c>true</c> if and only if the property has been set</returns>
        bool SetOption<T>(ChannelOption<T> option, T value);

        /// <summary>Gets or sets the connect timeout of the channel in milliseconds.  If the
        /// <see cref="IChannel"/> does not support connect operation, this property is not
        /// used at all, and therefore will be ignored.</summary>
        TimeSpan ConnectTimeout { get; set; }

        /// <summary>Gets or sets the maximum number of messages to read per read loop.
        /// If this value is greater than 1, an event loop might attempt to read multiple times to procure multiple messages.</summary>
        int MaxMessagesPerRead { get; set; }

        /// <summary>Gets or sets the maximum loop count for a write operation until
        /// {@link WritableByteChannel#write(ByteBuffer)} returns a non-zero value.
        /// It is similar to what a spin lock is used for in concurrency programming.
        /// It improves memory utilization and write throughput depending on
        /// the platform that JVM runs on.  The default value is {@code 16}.</summary>
        int WriteSpinCount { get; set; }

        /// <summary>Gets or sets the <see cref="IByteBufferAllocator"/> which is used for the channel to allocate buffers.</summary>
        IByteBufferAllocator Allocator { get; set; }

        /// <summary>Gets or sets the <see cref="IRecvByteBufAllocator"/> which is used for the channel to allocate receive buffers.</summary>
        IRecvByteBufAllocator RecvByteBufAllocator { get; set; }

        /// <summary>Gets or sets if <see cref="IChannelHandlerContext.Read()"/> will be invoked automatically so that a user application doesn't
        /// need to call it at all. The default value is <c>true</c>.</summary>
        bool AutoRead { get; set; }

        /// <summary>Gets or sets whether the <see cref="IChannel"/> should be closed automatically and immediately on write failure.
        /// The default is <c>true</c>.</summary>
        bool AutoClose { get; set; }

        /// <summary>Gets or sets the high water mark of the write buffer.  If the number of bytes
        /// queued in the write buffer exceeds this value, <see cref="IChannel.IsWritable"/>
        /// will start to return <c>false</c>.</summary>
        int WriteBufferHighWaterMark { get; set; }

        /// <summary>Gets or sets the low water mark of the write buffer. Once the number of bytes
        /// queued in the write buffer exceeded the <see cref="WriteBufferHighWaterMark"/> and then
        /// dropped down below this value, <see cref="IChannel.IsWritable"/> will start to return
        /// <c>true</c> again.</summary>
        int WriteBufferLowWaterMark { get; set; }

        /// <summary>Gets or sets the <see cref="IMessageSizeEstimator"/> which is used for the channel
        /// to detect the size of a message.</summary>
        IMessageSizeEstimator MessageSizeEstimator { get; set; }
    }
}