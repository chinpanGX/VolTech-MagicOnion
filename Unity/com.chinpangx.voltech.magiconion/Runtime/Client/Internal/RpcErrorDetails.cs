using System;
using System.Collections.Generic;
using System.IO;
using Google.Protobuf;
using Grpc.Core;
using VolTech.Server.Abstractions;

namespace VolTech.Client
{
    /// <summary>
    /// サーバーがエラーと一緒に返す詳細(リッチエラーモデル)を読む。
    /// </summary>
    /// <remarks>
    /// google.rpc.Status と google.rpc.ErrorInfo は、Google.Api.CommonProtos を使わずに読み書きする。
    /// CommonProtos はビルド済みの DLL で、参照する Google.Protobuf の版が決まっている。Unity のエディタは別の版の
    /// Google.Protobuf を同梱していて、版がずれるとエディタの中で ErrorInfo を読めなくなる(業務エラーがすべて CLIENT_ERROR になる)。
    /// </remarks>
    internal static class RpcErrorDetails
    {
        /// <summary>サーバーがエラーの詳細を入れるトレーラーのキー。</summary>
        internal const string TrailerKey = ApiHeaders.ErrorDetailsTrailer;

        private const string ErrorInfoTypeName = "google.rpc.ErrorInfo";
        private const string TypeUrlPrefix = "type.googleapis.com/";

        // google.rpc.Status: int32 code = 1; string message = 2; repeated google.protobuf.Any details = 3;
        private const uint StatusCodeTag = 8;
        private const uint StatusMessageTag = 18;
        private const uint StatusDetailsTag = 26;

        // google.protobuf.Any: string type_url = 1; bytes value = 2;
        private const uint AnyTypeUrlTag = 10;
        private const uint AnyValueTag = 18;

        // google.rpc.ErrorInfo: string reason = 1; string domain = 2; map<string, string> metadata = 3;
        private const uint ErrorInfoReasonTag = 10;
        private const uint ErrorInfoDomainTag = 18;
        private const uint ErrorInfoMetadataTag = 26;

        // map の1項目: key = 1; value = 2;
        private const uint MapKeyTag = 10;
        private const uint MapValueTag = 18;

        /// <summary>
        /// トレーラーの google.rpc.Status から ErrorInfo を取り出す。無ければ null を返す。
        /// </summary>
        /// <exception cref="InvalidProtocolBufferException">詳細が壊れていて読めないとき。</exception>
        internal static RpcErrorInfo GetErrorInfo(RpcException exception)
        {
            // Grpc.StatusProto は Unity NuGet レジストリに無いので、トレーラーを自分でパースする
            var details = exception.Trailers?.GetValueBytes(TrailerKey);
            if (details == null)
            {
                return null;
            }

            return ParseStatus(details);
        }

        /// <summary>
        /// ErrorInfo を1つ入れた google.rpc.Status を作る(偽のサーバーが返すエラー用)。
        /// </summary>
        internal static byte[] EncodeStatus(int code, string message, RpcErrorInfo errorInfo)
        {
            var errorInfoBytes = Encode(output =>
            {
                WriteString(output, ErrorInfoReasonTag, errorInfo.Reason);
                WriteString(output, ErrorInfoDomainTag, errorInfo.Domain);
                foreach (var pair in errorInfo.Metadata)
                {
                    var entry = Encode(entryOutput =>
                    {
                        WriteString(entryOutput, MapKeyTag, pair.Key);
                        WriteString(entryOutput, MapValueTag, pair.Value);
                    });
                    WriteBytes(output, ErrorInfoMetadataTag, entry);
                }
            });
            var any = Encode(output =>
            {
                WriteString(output, AnyTypeUrlTag, TypeUrlPrefix + ErrorInfoTypeName);
                WriteBytes(output, AnyValueTag, errorInfoBytes);
            });
            return Encode(output =>
            {
                if (code != 0)
                {
                    output.WriteTag(StatusCodeTag);
                    output.WriteInt32(code);
                }

                WriteString(output, StatusMessageTag, message);
                WriteBytes(output, StatusDetailsTag, any);
            });
        }

        // details の中から最初の ErrorInfo を探す。code と message は gRPC のステータスと同じなので読まない
        private static RpcErrorInfo ParseStatus(byte[] bytes)
        {
            var input = new CodedInputStream(bytes);
            RpcErrorInfo found = null;
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                if (tag != StatusDetailsTag)
                {
                    input.SkipLastField();
                    continue;
                }

                // 最初の1つを使う。壊れていないことを確かめるために、残りも読む
                var errorInfo = ParseAny(input.ReadBytes());
                found ??= errorInfo;
            }

            return found;
        }

        // ErrorInfo の Any なら中身を読む。ほかの型なら null を返す
        private static RpcErrorInfo ParseAny(ByteString bytes)
        {
            var input = bytes.CreateCodedInput();
            var typeUrl = "";
            var value = ByteString.Empty;
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                switch (tag)
                {
                    case AnyTypeUrlTag:
                        typeUrl = input.ReadString();
                        break;
                    case AnyValueTag:
                        value = input.ReadBytes();
                        break;
                    default:
                        input.SkipLastField();
                        break;
                }
            }

            // type_url の最後の / より後ろが型の名前(Google.Protobuf の Any.Is と同じ見方)
            var typeName = typeUrl.Substring(typeUrl.LastIndexOf('/') + 1);
            return typeName == ErrorInfoTypeName ? ParseErrorInfo(value) : null;
        }

        private static RpcErrorInfo ParseErrorInfo(ByteString bytes)
        {
            var input = bytes.CreateCodedInput();
            var reason = "";
            var domain = "";
            var metadata = new Dictionary<string, string>();
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                switch (tag)
                {
                    case ErrorInfoReasonTag:
                        reason = input.ReadString();
                        break;
                    case ErrorInfoDomainTag:
                        domain = input.ReadString();
                        break;
                    case ErrorInfoMetadataTag:
                        var (key, value) = ParseMapEntry(input.ReadBytes());
                        // 同じキーが2度来たら後のものを使う(protobuf の map と同じ)
                        metadata[key] = value;
                        break;
                    default:
                        input.SkipLastField();
                        break;
                }
            }

            return new RpcErrorInfo(reason, domain, metadata);
        }

        private static (string Key, string Value) ParseMapEntry(ByteString bytes)
        {
            var input = bytes.CreateCodedInput();
            var key = "";
            var value = "";
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                switch (tag)
                {
                    case MapKeyTag:
                        key = input.ReadString();
                        break;
                    case MapValueTag:
                        value = input.ReadString();
                        break;
                    default:
                        input.SkipLastField();
                        break;
                }
            }

            return (key, value);
        }

        private static byte[] Encode(Action<CodedOutputStream> write)
        {
            using var stream = new MemoryStream();
            using (var output = new CodedOutputStream(stream, true))
            {
                write(output);
            }

            return stream.ToArray();
        }

        // protobuf は既定値(空の文字列)を書かない
        private static void WriteString(CodedOutputStream output, uint tag, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            output.WriteTag(tag);
            output.WriteString(value);
        }

        private static void WriteBytes(CodedOutputStream output, uint tag, byte[] value)
        {
            output.WriteTag(tag);
            output.WriteBytes(ByteString.CopyFrom(value));
        }
    }
}
