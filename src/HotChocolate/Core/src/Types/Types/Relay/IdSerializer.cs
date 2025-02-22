using System;
using System.Text;
using System.Buffers;
using System.Buffers.Text;
using HotChocolate.Language;
using HotChocolate.Properties;

namespace HotChocolate.Types.Relay
{
    public sealed class IdSerializer : IIdSerializer
    {
        private const int _stackallocThreshold = 256;
        private const int _divisor = 4;
        private const byte _separator = (byte)'\n';
        private const byte _guid = (byte)'g';
        private const byte _short = (byte)'s';
        private const byte _int = (byte)'i';
        private const byte _long = (byte)'l';
        private const byte _default = (byte)'d';
        private const char _equals = '=';
        private const byte _schema = 0;

        private static readonly Encoding _utf8 = Encoding.UTF8;

        private readonly bool _includeSchemaName;

        public IdSerializer(bool includeSchemaName = false)
        {
            _includeSchemaName = includeSchemaName;
        }

        public string Serialize<T>(NameString typeName, T id) =>
            Serialize(default, typeName, id);

        public string Serialize<T>(NameString schemaName, NameString typeName, T id)
        {
            if (id is null)
            {
                return null;
            }

            typeName.EnsureNotEmpty(nameof(typeName));
            schemaName = schemaName.HasValue ? schemaName : Schema.DefaultName;

            string idString = null;

            switch (id)
            {
                case Guid:
                case short:
                case int:
                case long:
                    break;

                case string s:
                    idString = s;
                    break;

                default:
                    idString = id.ToString();
                    break;
            }

            var schemaSize = _includeSchemaName
                ? GetAllocationSize(schemaName.Value)
                : 0;

            var nameSize = GetAllocationSize(typeName.Value);

            var idSize = idString is null
                ? GetAllocationSize(in id)
                : GetAllocationSize(in idString);

            var serializedSize = ((schemaSize + nameSize + idSize + 16) / 3) * 4;

            byte[] serializedArray = null;

            Span<byte> serialized = serializedSize <= _stackallocThreshold
                ? stackalloc byte[serializedSize]
                : (serializedArray = ArrayPool<byte>.Shared.Rent(serializedSize));

            try
            {
                var position = 0;

                if (_includeSchemaName)
                {
                    serialized[position++] = _schema;
                    position += CopyString(schemaName.Value,
                        serialized.Slice(position, schemaSize));
                    serialized[position++] = _separator;
                }

                position += CopyString(typeName.Value,
                    serialized.Slice(position, nameSize));
                serialized[position++] = _separator;

                Span<byte> value = serialized.Slice(position + 1);

                int bytesWritten;
                switch (id)
                {
                    case Guid g:
                        serialized[position++] = _guid;
                        Utf8Formatter.TryFormat(g, value, out bytesWritten, 'N');
                        position += idSize;
                        break;

                    case short s:
                        serialized[position++] = _short;
                        Utf8Formatter.TryFormat(s, value, out bytesWritten);
                        position += bytesWritten;
                        break;

                    case int i:
                        serialized[position++] = _int;
                        Utf8Formatter.TryFormat(i, value, out bytesWritten);
                        position += bytesWritten;
                        break;

                    case long l:
                        serialized[position++] = _long;
                        Utf8Formatter.TryFormat(l, value, out bytesWritten);
                        position += bytesWritten;
                        break;

                    default:
                        serialized[position++] = _default;
                        position += CopyString(idString, value);
                        break;
                }

                OperationStatus operationStatus =
                    Base64.EncodeToUtf8InPlace(serialized, position, out bytesWritten);

                if (operationStatus != OperationStatus.Done)
                {
                    throw new IdSerializationException(
                        TypeResources.IdSerializer_UnableToEncode,
                        operationStatus,
                        idString);
                }

                serialized = serialized.Slice(0, bytesWritten);

                return CreateString(serialized);
            }
            finally
            {
                if (serializedArray != null)
                {
                    serialized.Clear();
                    ArrayPool<byte>.Shared.Return(serializedArray);
                }
            }
        }

        private unsafe int CopyString(string value, Span<byte> serialized)
        {
            fixed (byte* bytePtr = serialized)
            {
                fixed (char* charPtr = value)
                {
                    return _utf8.GetBytes(
                        charPtr, value.Length,
                        bytePtr, serialized.Length);
                }
            }
        }

        private unsafe string CreateString(Span<byte> serialized)
        {
            fixed (byte* bytePtr = serialized)
            {
                return _utf8.GetString(bytePtr, serialized.Length);
            }
        }

        public IdValue Deserialize(string serializedId)
        {
            if (serializedId is null)
            {
                throw new ArgumentNullException(nameof(serializedId));
            }

            var serializedSize = GetAllocationSize(serializedId);

            byte[] serializedArray = null;

            Span<byte> serialized = serializedSize <= _stackallocThreshold
                ? stackalloc byte[serializedSize]
                : (serializedArray = ArrayPool<byte>.Shared.Rent(serializedSize));

            try
            {
                var bytesWritten = CopyString(serializedId, serialized);
                serialized = serialized.Slice(0, bytesWritten);

                OperationStatus operationStatus =
                    Base64.DecodeFromUtf8InPlace(serialized, out bytesWritten);

                if (operationStatus != OperationStatus.Done)
                {
                    throw new IdSerializationException(
                        TypeResources.IdSerializer_UnableToDecode,
                        operationStatus,
                        serializedId);
                }

                int nextSeparator;

                Span<byte> decoded = serialized.Slice(0, bytesWritten);

                NameString schemaName = null;

                if (decoded[0] == _schema)
                {
                    decoded = decoded.Slice(1);
                    nextSeparator = NextSeparator(decoded);
                    schemaName = CreateString(decoded.Slice(0, nextSeparator));
                    decoded = decoded.Slice(nextSeparator + 1);
                }

                nextSeparator = NextSeparator(decoded);
                NameString typeName = CreateString(decoded.Slice(0, nextSeparator));
                decoded = decoded.Slice(nextSeparator + 1);

                object value;

                switch (decoded[0])
                {
                    case _guid:
                        Utf8Parser.TryParse(decoded.Slice(1), out Guid g, out _, 'N');
                        value = g;
                        break;
                    case _short:
                        Utf8Parser.TryParse(decoded.Slice(1), out short s, out _);
                        value = s;
                        break;
                    case _int:
                        Utf8Parser.TryParse(decoded.Slice(1), out int i, out _);
                        value = i;
                        break;
                    case _long:
                        Utf8Parser.TryParse(decoded.Slice(1), out long l, out _);
                        value = l;
                        break;
                    default:
                        value = CreateString(decoded.Slice(1));
                        break;
                }

                return new IdValue(schemaName, typeName, value);
            }
            finally
            {
                if (serializedArray != null)
                {
                    serialized.Clear();
                    ArrayPool<byte>.Shared.Return(serializedArray);
                }
            }
        }

        public static bool IsPossibleBase64String(string s)
        {
            if (s is null)
            {
                throw new ArgumentNullException(nameof(s));
            }

            if (s.Length % _divisor != 0)
            {
                return false;
            }

            var equalsCount = 0;

            for (var i = 0; i < s.Length; i++)
            {
                if (IsBase64Char(s[i]))
                {
                    if (equalsCount > 0)
                    {
                        return false;
                    }
                }
                else if (s[i] == _equals)
                {
                    equalsCount++;
                }
            }

            return equalsCount == 0 || equalsCount % _divisor > 0;
        }

        private static bool IsBase64Char(in char c)
        {
            var b = (byte)c;
            return (b.IsLetterOrUnderscore() && b != GraphQLConstants.Underscore)
                || b.IsDigit()
                || c == GraphQLConstants.Dollar
                || c == GraphQLConstants.ForwardSlash;
        }

        private static int GetAllocationSize<T>(in T value)
        {
            return value switch
            {
                Guid => 32,
                short => 6,
                int => 11,
                long => 20,
                string s => _utf8.GetByteCount(s),
                _ => throw new NotSupportedException(),
            };
        }

        private static int NextSeparator(ReadOnlySpan<byte> serializedId)
        {
            for (var i = 0; i < serializedId.Length; i++)
            {
                if (serializedId[i] == _separator)
                {
                    return i;
                }
            }

            throw new InvalidOperationException("Invalid string sequence.");
        }
    }
}
