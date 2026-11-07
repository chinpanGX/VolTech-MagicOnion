using System.Collections;
using Cysharp.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using NUnit.Framework;
using UnityEngine.TestTools;
using VolTech.Client;
using VolTech.Client.Unity;
using VolTech.Server.Abstractions;

namespace VolTech.Sandbox.Sample
{
    /// <summary>
    /// サンプルの流れを確かめる。samples/VolTech.Sandbox.Server を起動してから動かす。
    /// </summary>
    /// <remarks>
    /// 時刻はサーバー全体の状態なので、テストの前後で元に戻す。プレイヤーはテストごとに新しい。
    /// </remarks>
    [Description("サンプルの流れを確かめる")]
    public class SampleFlowTests
    {
        private const int OneDay = 24 * 60 * 60;
        private const long MaxOffset = 3650L * OneDay;

        private GrpcChannel clockChannel;
        private IServerClockService clock;
        private CountingDialog dialog;
        private CountingNavigator navigator;
        private SampleSession session;

        /// <summary>
        /// 時刻を元に戻し、新しいプレイヤーを作る。
        /// </summary>
        [UnitySetUp]
        public IEnumerator SetUp() => UniTask.ToCoroutine(async () =>
        {
            clockChannel = ApiChannel.Create(SampleClients.Address);
            clock = SampleClients.CreateClockClient(ApiChannel.CreateCallInvoker(clockChannel));
            await clock.SetOffsetAsync(0);
            dialog = new CountingDialog();
            navigator = new CountingNavigator();
            session = SampleSetup.Create(SampleClients.Address, dialog, navigator);
        });

        /// <summary>
        /// 時刻を元に戻す。
        /// </summary>
        [UnityTearDown]
        public IEnumerator TearDown() => UniTask.ToCoroutine(async () =>
        {
            await clock.SetOffsetAsync(0);
            session.Dispose();
            clockChannel.Dispose();
        });

        /// <summary>
        /// 日付が変わると DATE_CHANGED でタイトルに戻り、入り直すと無料ガチャを引ける。
        /// </summary>
        [UnityTest]
        [Description("日付が変わると DATE_CHANGED でタイトルに戻り、入り直すと無料ガチャを引ける")]
        public IEnumerator DateChangeFlow() => UniTask.ToCoroutine(async () =>
        {
            var home = await session.EnterAsync();
            Assert.That(home.IsError, Is.False, home.ErrorMessage);
            AssertPlayer(3000, freeDrawAvailable: true);

            var paid = await session.DrawAsync(useFree: false);
            Assert.That(paid.IsError, Is.False, paid.ErrorMessage);
            Assert.That(paid.Value.CharacterName, Is.Not.Empty);
            AssertPlayer(2700, freeDrawAvailable: true);

            var free = await session.DrawAsync(useFree: true);
            Assert.That(free.IsError, Is.False, free.ErrorMessage);
            Assert.That(free.Value.Common, Is.Null);
            AssertPlayer(2700, freeDrawAvailable: false);

            var used = await session.DrawAsync(useFree: true);
            Assert.That(used.ErrorCode, Is.EqualTo(SampleErrors.FreeDrawUsed));
            Assert.That(dialog.Count, Is.Zero);

            var paidAfterFree = await session.DrawAsync(useFree: false);
            Assert.That(paidAfterFree.IsError, Is.False, paidAfterFree.ErrorMessage);
            AssertPlayer(2400, freeDrawAvailable: false);

            await clock.SetOffsetAsync(OneDay);
            var changed = await session.DrawAsync(useFree: false);
            Assert.That(changed.ErrorCode, Is.EqualTo(ReservedErrors.DateChanged));
            Assert.That(navigator.TitleCount, Is.EqualTo(1));
            AssertPlayer(2400, freeDrawAvailable: false);

            var reentered = await session.EnterAsync();
            Assert.That(reentered.IsError, Is.False, reentered.ErrorMessage);
            AssertPlayer(2400, freeDrawAvailable: true);

            var nextFree = await session.DrawAsync(useFree: true);
            Assert.That(nextFree.IsError, Is.False, nextFree.ErrorMessage);
            AssertPlayer(2400, freeDrawAvailable: false);
        });

        /// <summary>
        /// ジェムが足りなければ NOT_ENOUGH_GEMS になり、ジェムは 0 より減らない。
        /// </summary>
        [UnityTest]
        [Description("ジェムが足りなければ NOT_ENOUGH_GEMS になり、ジェムは 0 より減らない")]
        public IEnumerator NotEnoughGems() => UniTask.ToCoroutine(async () =>
        {
            await session.EnterAsync();
            for (var i = 0; i < 10; i++)
            {
                var result = await session.DrawAsync(useFree: false);
                Assert.That(result.IsError, Is.False, result.ErrorMessage);
            }

            var failed = await session.DrawAsync(useFree: false);

            Assert.That(failed.ErrorCode, Is.EqualTo(SampleErrors.NotEnoughGems));
            Assert.That(dialog.Count, Is.Zero);
            await session.EnterAsync();
            Assert.That(session.Player.Gems, Is.Zero);
        });

        /// <summary>
        /// 先の日付で無料で引いてから時刻を戻すと、同じ日にもう一度引ける。
        /// </summary>
        [UnityTest]
        [Description("先の日付で無料で引いてから時刻を戻すと、同じ日にもう一度引ける")]
        public IEnumerator ResetClockFlow() => UniTask.ToCoroutine(async () =>
        {
            await session.EnterAsync();
            await session.DrawAsync(useFree: true);

            await clock.SetOffsetAsync(OneDay);
            await session.DrawAsync(useFree: false);
            await session.EnterAsync();
            await session.DrawAsync(useFree: true);

            await clock.SetOffsetAsync(0);
            var changed = await session.DrawAsync(useFree: false);
            Assert.That(changed.ErrorCode, Is.EqualTo(ReservedErrors.DateChanged));

            await session.EnterAsync();

            Assert.That(session.Player.FreeDrawAvailable, Is.True);
        });

        /// <summary>
        /// 時刻の RPC はトークン無しで呼べて、変更後の日付を返す。範囲の外は拒む。
        /// </summary>
        [UnityTest]
        [Description("時刻の RPC はトークン無しで呼べて、変更後の日付を返す。範囲の外は拒む")]
        public IEnumerator ClockRpc() => UniTask.ToCoroutine(async () =>
        {
            var before = await clock.GetAsync();

            var shifted = await clock.SetOffsetAsync(OneDay);

            Assert.That(shifted.OffsetSeconds, Is.EqualTo(OneDay));
            Assert.That(shifted.GameDate, Is.Not.EqualTo(before.GameDate));
            Assert.That((await clock.GetAsync()).GameDate, Is.EqualTo(shifted.GameDate));

            Assert.That((await clock.SetOffsetAsync(MaxOffset)).OffsetSeconds, Is.EqualTo(MaxOffset));
            Assert.That(await StatusOfAsync(-1), Is.EqualTo(StatusCode.InvalidArgument));
            Assert.That(await StatusOfAsync(MaxOffset + 1), Is.EqualTo(StatusCode.InvalidArgument));
        });

        private async UniTask<StatusCode> StatusOfAsync(long seconds)
        {
            try
            {
                await clock.SetOffsetAsync(seconds);
                return StatusCode.OK;
            }
            catch (RpcException exception)
            {
                return exception.StatusCode;
            }
        }

        private void AssertPlayer(int gems, bool freeDrawAvailable)
        {
            Assert.That(session.Player.Gems, Is.EqualTo(gems));
            Assert.That(session.Player.FreeDrawAvailable, Is.EqualTo(freeDrawAvailable));
        }

        // 帯の代わり。回数を数え、すぐボタンを押す
        private sealed class CountingDialog : IErrorDialogView
        {
            public int Count { get; private set; }

            public UniTask<ErrorDialogButton> ShowAsync(ApiError error, ErrorDialogButtons buttons)
            {
                Count++;
                return UniTask.FromResult(buttons == ErrorDialogButtons.Ok ? ErrorDialogButton.Ok : ErrorDialogButton.Title);
            }
        }

        private sealed class CountingNavigator : IErrorNavigator
        {
            public int TitleCount { get; private set; }

            public UniTask GoToTitleAsync()
            {
                TitleCount++;
                return UniTask.CompletedTask;
            }

            public UniTask GoToHomeAsync()
            {
                return UniTask.CompletedTask;
            }

            public UniTask OpenStoreAsync()
            {
                return UniTask.CompletedTask;
            }
        }
    }
}
