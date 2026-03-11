// Copyright (c) Wieslaw Soltes. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

#nullable disable

using System;
using System.Collections.Generic;
using System.Text;

namespace Avalonia.Controls
{
    /// <summary>
    /// Provides persistence helpers to capture/restore DataGrid state as string or byte[] payloads.
    /// </summary>
    #if !DATAGRID_INTERNAL
    public
    #else
    internal
    #endif
    static class DataGridStatePersistence
    {
        private static readonly IDataGridStateSerializer s_defaultSerializer = new SystemTextJsonDataGridStateSerializer();

        /// <summary>
        /// Captures runtime DataGrid state and converts it to a serializer-friendly persisted model.
        /// </summary>
        public static DataGridPersistedState CaptureState(
            DataGrid grid,
            DataGridStateSections sections = DataGridStateSections.All,
            DataGridStateOptions stateOptions = null,
            DataGridStatePersistenceOptions persistenceOptions = null)
        {
            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            var runtimeState = grid.CaptureState(sections, stateOptions);
            return DataGridStatePersistenceMapper.ToPersisted(runtimeState, grid.ColumnDefinitionsSource.AsReadOnly(), stateOptions, persistenceOptions);
        }

        /// <summary>
        /// Restores DataGrid state from a persisted model.
        /// </summary>
        public static void RestoreState(
            DataGrid grid,
            DataGridPersistedState state,
            DataGridStateSections sections = DataGridStateSections.All,
            DataGridStateOptions stateOptions = null,
            DataGridStatePersistenceOptions persistenceOptions = null)
        {
            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            var runtimeState = DataGridStatePersistenceMapper.ToRuntime(state, sections, grid.ColumnDefinitionsSource.AsReadOnly(), stateOptions, persistenceOptions);
            grid.RestoreState(runtimeState, runtimeState.Sections, stateOptions);
        }

        /// <summary>
        /// Serializes a persisted state model to byte[].
        /// </summary>
        public static byte[] Serialize(
            DataGridPersistedState state,
            IDataGridStateSerializer serializer = null)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            return (serializer ?? s_defaultSerializer).Serialize(state);
        }

        /// <summary>
        /// Serializes a persisted state model to string.
        /// </summary>
        public static string SerializeToString(
            DataGridPersistedState state,
            IDataGridStateSerializer serializer = null)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            return (serializer ?? s_defaultSerializer).SerializeToString(state);
        }

        /// <summary>
        /// Deserializes a persisted state model from byte[].
        /// </summary>
        public static DataGridPersistedState Deserialize(
            ReadOnlySpan<byte> payload,
            IDataGridStateSerializer serializer = null)
        {
            return (serializer ?? s_defaultSerializer).Deserialize(payload);
        }

        /// <summary>
        /// Deserializes a persisted state model from string.
        /// </summary>
        public static DataGridPersistedState Deserialize(
            string payload,
            IDataGridStateSerializer serializer = null)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            return (serializer ?? s_defaultSerializer).Deserialize(payload);
        }

        /// <summary>
        /// Captures and serializes DataGrid state to byte[].
        /// </summary>
        public static byte[] SerializeState(
            DataGrid grid,
            DataGridStateSections sections = DataGridStateSections.All,
            DataGridStateOptions stateOptions = null,
            IDataGridStateSerializer serializer = null,
            DataGridStatePersistenceOptions persistenceOptions = null)
        {
            var state = CaptureState(grid, sections, stateOptions, persistenceOptions);
            return Serialize(state, serializer);
        }

        /// <summary>
        /// Captures and serializes DataGrid state to string.
        /// </summary>
        public static string SerializeStateToString(
            DataGrid grid,
            DataGridStateSections sections = DataGridStateSections.All,
            DataGridStateOptions stateOptions = null,
            IDataGridStateSerializer serializer = null,
            DataGridStatePersistenceOptions persistenceOptions = null)
        {
            var state = CaptureState(grid, sections, stateOptions, persistenceOptions);
            return SerializeToString(state, serializer);
        }

        /// <summary>
        /// Deserializes and restores DataGrid state from byte[] payload.
        /// </summary>
        public static void RestoreState(
            DataGrid grid,
            ReadOnlySpan<byte> payload,
            DataGridStateSections sections = DataGridStateSections.All,
            DataGridStateOptions stateOptions = null,
            IDataGridStateSerializer serializer = null,
            DataGridStatePersistenceOptions persistenceOptions = null)
        {
            var state = Deserialize(payload, serializer);
            RestoreState(grid, state, sections, stateOptions, persistenceOptions);
        }

        /// <summary>
        /// Deserializes and restores DataGrid state from string payload.
        /// </summary>
        public static void RestoreStateFromString(
            DataGrid grid,
            string payload,
            DataGridStateSections sections = DataGridStateSections.All,
            DataGridStateOptions stateOptions = null,
            IDataGridStateSerializer serializer = null,
            DataGridStatePersistenceOptions persistenceOptions = null)
        {
            var state = Deserialize(payload, serializer);
            RestoreState(grid, state, sections, stateOptions, persistenceOptions);
        }

        /// <summary>
        /// Encodes a binary state payload to Base64.
        /// </summary>
        public static string EncodeBase64(ReadOnlySpan<byte> payload)
        {
            return Convert.ToBase64String(payload.ToArray());
        }

        /// <summary>
        /// Decodes a Base64 state payload.
        /// </summary>
        public static byte[] DecodeBase64(string payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            return Convert.FromBase64String(payload);
        }

        /// <summary>
        /// Encodes a string as UTF8 bytes.
        /// </summary>
        public static byte[] ToUtf8Bytes(string payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            return Encoding.UTF8.GetBytes(payload);
        }

        /// <summary>
        /// Decodes UTF8 bytes to a string.
        /// </summary>
        public static string FromUtf8Bytes(ReadOnlySpan<byte> payload)
        {
            return Encoding.UTF8.GetString(payload);
        }
    }
}
