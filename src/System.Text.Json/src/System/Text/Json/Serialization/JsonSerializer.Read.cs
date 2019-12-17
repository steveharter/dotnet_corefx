﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace System.Text.Json
{
    /// <summary>
    /// Provides functionality to serialize objects or value types to JSON and
    /// deserialize JSON into objects or value types.
    /// </summary>
    public static partial class JsonSerializer
    {
        internal static void ReadCore(
            JsonSerializerOptions options,
            ref Utf8JsonReader reader,
            ref ReadStack state)
        {
            try
            {
                bool success;
                object value;

                JsonPropertyInfo jsonPropertyInfo = state.Current.JsonClassInfo.PolicyProperty;
                JsonConverter converter = jsonPropertyInfo.ConverterBase;
                if (state.IsContinuation)
                {
                    // If a continuation, continue to read ahead here to avoid having to build and
                    // then tear down the call stack if there is more than one buffer fetch necessary.
                    if (!JsonConverter.DoSingleValueReadWithReadAhead(ref reader, ref state))
                    {
                        state.BytesConsumed += reader.BytesConsumed;
                        return;
                    }
                }
                else
                {
                    if (!converter.SingleValueReadWithReadAhead(ref reader, ref state))
                    {
                        // Read more data until we have the full element.
                        state.BytesConsumed += reader.BytesConsumed;
                        return;
                    }
                }

                value = null;
                success = converter.TryReadAsObject(ref reader, jsonPropertyInfo.RuntimePropertyType, options, ref state, ref value);

                if (success)
                {
                    state.Current.ReturnValue = value;
                }
            }
            catch (JsonReaderException ex)
            {
                // Re-throw with Path information.
                ThrowHelper.ReThrowWithPath(state, ex);
            }
            catch (FormatException ex) when (ex.Source == ThrowHelper.ExceptionSourceValueToRethrowAsJsonException)
            {
                ThrowHelper.ReThrowWithPath(state, reader, ex);
            }
            catch (InvalidOperationException ex) when (ex.Source == ThrowHelper.ExceptionSourceValueToRethrowAsJsonException)
            {
                ThrowHelper.ReThrowWithPath(state, reader, ex);
            }
            catch (JsonException ex)
            {
                ThrowHelper.AddExceptionInformation(state, reader, ex);
                throw;
            }

            state.BytesConsumed += reader.BytesConsumed;
        }

        private static ReadOnlySpan<byte> GetUnescapedString(ReadOnlySpan<byte> utf8Source, int idx)
        {
            // The escaped name is always longer than the unescaped, so it is safe to use escaped name for the buffer length.
            int length = utf8Source.Length;
            byte[] pooledName = null;

            Span<byte> unescapedName = length <= JsonConstants.StackallocThreshold ?
                stackalloc byte[length] :
                (pooledName = ArrayPool<byte>.Shared.Rent(length));

            JsonReaderHelper.Unescape(utf8Source, unescapedName, idx, out int written);
            ReadOnlySpan<byte> propertyName = unescapedName.Slice(0, written).ToArray();

            if (pooledName != null)
            {
                // We clear the array because it is "user data" (although a property name).
                new Span<byte>(pooledName, 0, written).Clear();
                ArrayPool<byte>.Shared.Return(pooledName);
            }

            return propertyName;
        }
    }
}
