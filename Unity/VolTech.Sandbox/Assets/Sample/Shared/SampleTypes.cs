#nullable enable

using System;
using MessagePack;
using VolTech.Server.Abstractions;

namespace VolTech.Sandbox.Sample
{
    /// <summary>共通レスポンス項目。変わったときだけ入る。</summary>
    [MessagePackObject]
    public sealed class SampleCommon
    {
        /// <summary>変更後のジェム。</summary>
        [Key(0)]
        public int Gems { get; set; }
    }

    [MessagePackObject]
    public sealed class DrawRequest
    {
        /// <summary>今日の無料ガチャを使うか。</summary>
        [Key(0)]
        public bool UseFree { get; set; }
    }

    [MessagePackObject]
    public sealed class DrawResponse : IApiResponse<SampleCommon>
    {
        [Key(0)]
        public SampleCommon? Common { get; set; }

        [Key(1)]
        public string CharacterName { get; set; } = "";

        [Key(2)]
        public bool FreeDrawAvailable { get; set; }
    }

    /// <summary>ホームの全件。全件を返すので Common は使わない。</summary>
    [MessagePackObject]
    public sealed class HomeResponse
    {
        [Key(0)]
        public int Gems { get; set; }

        [Key(1)]
        public bool FreeDrawAvailable { get; set; }
    }

    [MessagePackObject]
    public sealed class ServerClockInfo
    {
        /// <summary>ずらした後のサーバーの時刻(UTC)。</summary>
        [Key(0)]
        public DateTime UtcNow { get; set; }

        /// <summary>ゲーム内の日付(yyyy-MM-dd)。</summary>
        [Key(1)]
        public string GameDate { get; set; } = "";

        /// <summary>本当の時刻からずらしている秒数。</summary>
        [Key(2)]
        public long OffsetSeconds { get; set; }
    }
}
