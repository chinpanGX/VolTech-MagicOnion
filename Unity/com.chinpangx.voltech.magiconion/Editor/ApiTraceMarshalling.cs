using System;
using System.Buffers;
using Grpc.Core;
using MessagePack;

namespace VolTech.Client.Editor
{
    /// <summary>
    /// メッセージと、記録に持つバイト列の変換。
    /// </summary>
    /// <remarks>
    /// MagicOnion のメッセージには protobuf の Clone が無いので、通信の時点の中身をバイト列で持つ。
    /// MagicOnion の Marshaller は Serializer を実装していないので、ContextualSerializer を使う。
    /// </remarks>
    internal static class ApiTraceMarshalling
    {
        /// <summary>
        /// メッセージをバイト列にする。
        /// </summary>
        public static byte[] Serialize<T>(Marshaller<T> marshaller, T value)
        {
            var context = new BufferSerializationContext();
            marshaller.ContextualSerializer(value, context);
            return context.Payload;
        }

        /// <summary>
        /// バイト列を、読まれたときに JSON にする Lazy を作る。JSON にできなくても、読んだ側(ウィンドウ)で例外にしない。
        /// </summary>
        public static Lazy<string> ToLazyJson<T>(Marshaller<T> marshaller, byte[] bytes)
        {
            return new Lazy<string>(() => Format(marshaller, bytes));
        }

        // 項目名を付けて JSON にする。できなければ、項目名の無い配列で出す
        private static string Format<T>(Marshaller<T> marshaller, byte[] bytes)
        {
            try
            {
                var value = marshaller.ContextualDeserializer(new BufferDeserializationContext(bytes));
                return ApiTraceJson.Format(value);
            }
            catch (Exception)
            {
                try
                {
                    return MessagePackSerializer.ConvertToJson(new ReadOnlyMemory<byte>(bytes));
                }
                catch (Exception exception)
                {
                    return $"(could not format as JSON: {exception.Message})";
                }
            }
        }

        private sealed class BufferSerializationContext : SerializationContext
        {
            private readonly ArrayBufferWriter<byte> writer = new();

            public byte[] Payload { get; private set; }

            public override IBufferWriter<byte> GetBufferWriter()
            {
                return writer;
            }

            public override void SetPayloadLength(int payloadLength)
            {
            }

            public override void Complete()
            {
                Payload = writer.WrittenSpan.ToArray();
            }

            public override void Complete(byte[] payload)
            {
                Payload = payload;
            }
        }

        private sealed class BufferDeserializationContext : DeserializationContext
        {
            private readonly byte[] payload;

            public BufferDeserializationContext(byte[] payload)
            {
                this.payload = payload;
            }

            public override int PayloadLength => payload.Length;

            public override byte[] PayloadAsNewBuffer()
            {
                return payload;
            }

            public override ReadOnlySequence<byte> PayloadAsReadOnlySequence()
            {
                return new ReadOnlySequence<byte>(payload);
            }
        }
    }
}
