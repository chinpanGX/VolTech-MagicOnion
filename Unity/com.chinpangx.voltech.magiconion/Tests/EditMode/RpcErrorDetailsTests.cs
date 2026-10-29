using System;
using System.Collections.Generic;
using Google.Protobuf;
using Grpc.Core;
using NUnit.Framework;
using VolTech.Client;
using VolTech.Server.Abstractions;

namespace VolTech.Client.Tests.EditMode
{
    /// <summary>
    /// RpcErrorDetails が、Google.Api.CommonProtos(google.rpc.Status / google.rpc.ErrorInfo)と同じ形で読み書きすることを確かめる。
    /// 期待するバイト列は、CommonProtos 2.17.0 で作ったもの。
    /// </summary>
    [Description("RpcErrorDetails が、Google.Api.CommonProtos と同じ形で google.rpc.Status / ErrorInfo を読み書きすることを確かめる")]
    public class RpcErrorDetailsTests
    {
        // Status{ code: 9, message: "メンテナンス中", details: [Any(BadRequest{}), Any(ErrorInfo{ reason: "MAINTENANCE", domain: "voltech",
        //   metadata: { end_at: "2026-10-11T12:00:00Z", action: "ERROR_ACTION_RETRY" } })] }
        private const string StatusWithOtherDetail =
            "CAkSFeODoeODs+ODhuODiuODs+OCueS4rRorCil0eXBlLmdvb2dsZWFwaXMuY29tL2dvb2dsZS5ycGMuQmFkUmVxdWVzdBqAAQoodHlwZS5nb29nbGVhcGlzLmNv" +
            "bS9nb29nbGUucnBjLkVycm9ySW5mbxJUCgtNQUlOVEVOQU5DRRIHdm9sdGVjaBoeCgZlbmRfYXQSFDIwMjYtMTAtMTFUMTI6MDA6MDBaGhwKBmFjdGlvbhIS" +
            "RVJST1JfQUNUSU9OX1JFVFJZ";

        // Status{ code: 9, message: "boom", details: [Any(ErrorInfo{ reason: "INSUFFICIENT_GEMS", domain: "sandbox",
        //   metadata: { end_at: "2026-10-11T12:00:00Z" } })] }
        private const string StatusWithErrorInfo =
            "CAkSBGJvb20aaAoodHlwZS5nb29nbGVhcGlzLmNvbS9nb29nbGUucnBjLkVycm9ySW5mbxI8ChFJTlNVRkZJQ0lFTlRfR0VNUxIHc2FuZGJveBoeCgZlbmRf" +
            "YXQSFDIwMjYtMTAtMTFUMTI6MDA6MDBa";

        /// <summary>
        /// CommonProtos で作った Status から、ほかの型の詳細を飛ばして ErrorInfo の reason・domain・metadata を読む。
        /// </summary>
        [Test]
        [Description("CommonProtos で作った Status から、ほかの型の詳細を飛ばして ErrorInfo の reason・domain・metadata を読む")]
        public void ReadsErrorInfoWrittenByCommonProtos()
        {
            var errorInfo = RpcErrorDetails.GetErrorInfo(WithDetails(Convert.FromBase64String(StatusWithOtherDetail)));

            Assert.That(errorInfo.Reason, Is.EqualTo("MAINTENANCE"));
            Assert.That(errorInfo.Domain, Is.EqualTo("voltech"));
            Assert.That(errorInfo.Metadata, Is.EquivalentTo(new Dictionary<string, string>
            {
                ["end_at"] = "2026-10-11T12:00:00Z",
                ["action"] = "ERROR_ACTION_RETRY",
            }));
        }

        /// <summary>
        /// FakeRpcErrors が作る Status は、CommonProtos で作ったものと同じバイト列になる。
        /// </summary>
        [Test]
        [Description("FakeRpcErrors が作る Status は、CommonProtos で作ったものと同じバイト列になる")]
        public void FakeErrorMatchesCommonProtos()
        {
            var exception = FakeRpcErrors.FailedPrecondition(
                new ErrorCode("sandbox", "INSUFFICIENT_GEMS"),
                "boom",
                new Dictionary<string, string> { ["end_at"] = "2026-10-11T12:00:00Z" });

            var details = exception.Trailers.GetValueBytes(RpcErrorDetails.TrailerKey);

            Assert.That(Convert.ToBase64String(details), Is.EqualTo(StatusWithErrorInfo));
        }

        /// <summary>
        /// トレーラーに詳細が無い、または詳細に ErrorInfo が無いときは null を返す。
        /// </summary>
        [Test]
        [Description("トレーラーに詳細が無い、または詳細に ErrorInfo が無いときは null を返す")]
        public void ReturnsNullWithoutErrorInfo()
        {
            var withoutTrailer = new RpcException(new Status(StatusCode.FailedPrecondition, ""));
            // Status{ code: 9 }(details が空)
            var withoutErrorInfo = WithDetails(new byte[] { 0x08, 0x09 });

            Assert.That(RpcErrorDetails.GetErrorInfo(withoutTrailer), Is.Null);
            Assert.That(RpcErrorDetails.GetErrorInfo(withoutErrorInfo), Is.Null);
        }

        /// <summary>
        /// 壊れた詳細は InvalidProtocolBufferException になる(ErrorConverter が UNKNOWN_ERROR にする)。
        /// </summary>
        [Test]
        [Description("壊れた詳細は InvalidProtocolBufferException になる(ErrorConverter が UNKNOWN_ERROR にする)")]
        public void BrokenDetailsThrowInvalidProtocolBufferException()
        {
            // 長さ5の項目と言いながら中身が無い
            var broken = WithDetails(new byte[] { 0x0A, 0x05 });

            Assert.Throws<InvalidProtocolBufferException>(() => RpcErrorDetails.GetErrorInfo(broken));
        }

        private static RpcException WithDetails(byte[] details)
        {
            var trailers = new Metadata { { RpcErrorDetails.TrailerKey, details } };
            return new RpcException(new Status(StatusCode.FailedPrecondition, ""), trailers);
        }
    }
}
