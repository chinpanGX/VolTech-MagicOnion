using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using MagicOnion.Client;
using MessagePack;
using NUnit.Framework;
using UnityEngine.TestTools;
using VolTech.Client.Editor;

namespace VolTech.Client.Tests.EditMode
{
    /// <summary>
    /// Body の JSON の作り方(ApiTraceJson・ApiTraceMarshalling)を確かめる。
    /// </summary>
    [Description("Body の JSON の作り方(ApiTraceJson・ApiTraceMarshalling)を確かめる")]
    public class ApiTraceJsonTests
    {
        private enum Color
        {
            Red,
            Blue,
        }

        /// <summary>
        /// 葉の値はそのまま出す。
        /// </summary>
        [Test]
        [Description("葉の値はそのまま出す")]
        public void LeavesAreWrittenAsIs()
        {
            Assert.That(ApiTraceJson.Format(null), Is.EqualTo("null"));
            Assert.That(ApiTraceJson.Format(Nil.Default), Is.EqualTo("null"));
            Assert.That(ApiTraceJson.Format("text"), Is.EqualTo("\"text\""));
            Assert.That(ApiTraceJson.Format(true), Is.EqualTo("true"));
            Assert.That(ApiTraceJson.Format(5), Is.EqualTo("5"));
            Assert.That(ApiTraceJson.Format(-5L), Is.EqualTo("-5"));
            Assert.That(ApiTraceJson.Format(ulong.MaxValue), Is.EqualTo("18446744073709551615"));
            Assert.That(ApiTraceJson.Format(1.5f), Is.EqualTo("1.5"));
            Assert.That(ApiTraceJson.Format(2.5d), Is.EqualTo("2.5"));
        }

        /// <summary>
        /// JSON で表せない数は文字列にする。
        /// </summary>
        [Test]
        [Description("JSON で表せない数は文字列にする")]
        public void NonFiniteNumbersBecomeStrings()
        {
            Assert.That(ApiTraceJson.Format(float.NaN), Is.EqualTo("\"NaN\""));
            Assert.That(ApiTraceJson.Format(double.PositiveInfinity), Is.EqualTo("\"Infinity\""));
        }

        /// <summary>
        /// enum は名前、DateTime は ISO 8601、byte[] は base64 の文字列にする。
        /// </summary>
        [Test]
        [Description("enum は名前、DateTime は ISO 8601、byte[] は base64 の文字列にする")]
        public void SpecialValuesBecomeStrings()
        {
            Assert.That(ApiTraceJson.Format(Color.Blue), Is.EqualTo("\"Blue\""));
            Assert.That(
                ApiTraceJson.Format(new DateTime(2026, 11, 8, 1, 2, 3, DateTimeKind.Utc)),
                Is.EqualTo("\"2026-11-08T01:02:03.0000000Z\""));
            Assert.That(ApiTraceJson.Format(new byte[] { 1, 2 }), Is.EqualTo("\"AQI=\""));
        }

        /// <summary>
        /// 文字列はエスケープする。日本語はそのまま出す。
        /// </summary>
        [Test]
        [Description("文字列はエスケープする。日本語はそのまま出す")]
        public void StringsAreEscaped()
        {
            Assert.That(ApiTraceJson.Format("アカリ\"\n\\"), Is.EqualTo("\"アカリ\\\"\\n\\\\\""));
        }

        /// <summary>
        /// [Key] のメンバーを項目名付きで、キーの小さい順に出す。基底クラスと public でないメンバーも出す。入れ子もたどる。
        /// </summary>
        [Test]
        [Description("[Key] のメンバーを項目名付きで、キーの小さい順に出す")]
        public void ObjectsAreWrittenWithMemberNames()
        {
            var value = new DerivedMessage("hidden")
            {
                First = 1,
                Third = 3,
                Inner = new InnerMessage { Value = "inner" },
                Ignored = 9,
            };

            Assert.That(
                ApiTraceJson.Format(value),
                Is.EqualTo("{\"First\":1,\"second\":\"hidden\",\"Third\":3,\"Inner\":{\"Value\":\"inner\"}}"));
        }

        /// <summary>
        /// 既定値の項目も出す。
        /// </summary>
        [Test]
        [Description("既定値の項目も出す")]
        public void DefaultValuesAreWritten()
        {
            Assert.That(ApiTraceJson.Format(new InnerMessage()), Is.EqualTo("{\"Value\":null}"));
        }

        /// <summary>
        /// IDictionary は項目名付きで、IEnumerable は配列で出す。
        /// </summary>
        [Test]
        [Description("IDictionary は項目名付きで、IEnumerable は配列で出す")]
        public void CollectionsAreWritten()
        {
            var dictionary = new Dictionary<int, InnerMessage> { { 1, new InnerMessage { Value = "a" } } };

            Assert.That(ApiTraceJson.Format(dictionary), Is.EqualTo("{\"1\":{\"Value\":\"a\"}}"));
            Assert.That(ApiTraceJson.Format(new List<int> { 1, 2 }), Is.EqualTo("[1,2]"));
        }

        /// <summary>
        /// 表に無い型は例外にする。
        /// </summary>
        [Test]
        [Description("表に無い型は例外にする")]
        public void UnsupportedTypesThrow()
        {
            Assert.Throws<NotSupportedException>(() => ApiTraceJson.Format(Guid.Empty));
            Assert.Throws<NotSupportedException>(() => ApiTraceJson.Format(new StringKeyMessage()));
        }

        /// <summary>
        /// 表に無い型を含むと、項目名の無い配列に切り替わる。
        /// </summary>
        [Test]
        [Description("表に無い型を含むと、項目名の無い配列に切り替わる")]
        public void UnsupportedValueFallsBackToArray()
        {
            var bytes = MessagePackSerializer.Serialize(new[] { 1, 2 });
            var marshaller = Marshallers.Create<object>(_ => bytes, _ => Guid.Empty);

            Assert.That(ApiTraceMarshalling.ToLazyJson(marshaller, bytes).Value, Is.EqualTo("[1,2]"));
        }

        /// <summary>
        /// 戻せないバイト列でも、配列で出す。
        /// </summary>
        [Test]
        [Description("戻せないバイト列でも、配列で出す")]
        public void UndeserializableBytesFallBackToArray()
        {
            var bytes = MessagePackSerializer.Serialize(new[] { 1, 2 });
            var marshaller = Marshallers.Create<object>(_ => bytes, _ => throw new InvalidOperationException("broken"));

            Assert.That(ApiTraceMarshalling.ToLazyJson(marshaller, bytes).Value, Is.EqualTo("[1,2]"));
        }

        /// <summary>
        /// MessagePack でないバイト列は、読めなかった旨を出す。例外にしない。
        /// </summary>
        [Test]
        [Description("MessagePack でないバイト列は、読めなかった旨を出す")]
        public void BrokenBytesAreReported()
        {
            var bytes = new byte[] { 0xc1 };
            var marshaller = Marshallers.Create<object>(_ => bytes, _ => throw new InvalidOperationException("broken"));

            Assert.That(ApiTraceMarshalling.ToLazyJson(marshaller, bytes).Value, Does.StartWith("(could not format as JSON: "));
        }

        /// <summary>
        /// MagicOnion の包みは中身だけを出す。複数の引数は配列になる。
        /// </summary>
        [UnityTest]
        [Description("MagicOnion の包みは中身だけを出す。複数の引数は配列になる")]
        public IEnumerator MagicOnionWrappersAreUnwrapped() => UniTask.ToCoroutine(async () =>
        {
            var store = new ApiTraceStore();
            var payload = default(byte[]);
            var invoker = new FakeCallInvoker { Handler = _ => Task.FromResult(FakeResult.Success(payload)) };
            var client = MagicOnionClient.Create<IClientBehaviorService>(
                invoker.Intercept(new ApiTraceInterceptor(store)),
                TestClients.SerializerProvider,
                Array.Empty<IClientFilter>(),
                TestClients.ClientFactoryProvider);

            payload = MessagePackSerializer.Serialize(3, TestClients.SerializerOptions);
            await client.CountAsync();
            payload = MessagePackSerializer.Serialize(Nil.Default, TestClients.SerializerOptions);
            await client.PingAsync();
            payload = MessagePackSerializer.Serialize(new ClientBehaviorReply { Value = 7 }, TestClients.SerializerOptions);
            await client.GetAsync(5);
            await client.TwoAsync("name", 5);

            var traces = store.Snapshot();
            Assert.That(traces.Select(trace => trace.RequestBody), Is.EqualTo(new[] { "null", "null", "5", "[\"name\",5]" }));
            Assert.That(traces[0].ResponseBody, Is.EqualTo("3"));
            Assert.That(traces[1].ResponseBody, Is.EqualTo("null"));
        });

        [MessagePackObject(SuppressSourceGeneration = true)]
        public class BaseMessage
        {
            [Key(1)]
            private readonly string second;

            protected BaseMessage(string second)
            {
                this.second = second;
            }

            [Key(0)]
            public int First { get; set; }
        }

        [MessagePackObject(SuppressSourceGeneration = true)]
        public sealed class DerivedMessage : BaseMessage
        {
            public DerivedMessage(string second)
                : base(second)
            {
            }

            [Key(3)]
            public InnerMessage Inner { get; set; }

            [Key(2)]
            public int Third { get; set; }

            [IgnoreMember]
            public int Ignored { get; set; }
        }

        [MessagePackObject(SuppressSourceGeneration = true)]
        public sealed class InnerMessage
        {
            [Key(0)]
            public string Value { get; set; }
        }

        [MessagePackObject(SuppressSourceGeneration = true)]
        public sealed class StringKeyMessage
        {
            [Key("name")]
            public int Name { get; set; }
        }
    }
}
