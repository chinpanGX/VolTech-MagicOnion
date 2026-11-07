using VolTech.Server.Abstractions;

namespace VolTech.Sandbox.Sample
{
    /// <summary>サンプルの業務エラー。</summary>
    public static class SampleErrors
    {
        public const string Domain = "sample";

        public static ErrorCode NotEnoughGems => new ErrorCode(Domain, "NOT_ENOUGH_GEMS");

        public static ErrorCode FreeDrawUsed => new ErrorCode(Domain, "FREE_DRAW_USED");
    }
}
