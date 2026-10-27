using System;

namespace VolTech.Server.Abstractions
{
    /// <summary>
    /// 認証が要らない RPC(サービス)に付ける。デバイス登録・トークン発行など。
    /// サーバーの AuthFilter とクライアントの設定の両方が、この属性を読む。
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class SkipAuthAttribute : Attribute
    {
    }

    /// <summary>
    /// 副作用が無い(読み取り専用の)RPC に付ける。付けていない RPC は冪等性の対象になり、
    /// 同じ x-request-id の再送で、本体を呼ばずに保存済みのレスポンスを返す。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class NoSideEffectsAttribute : Attribute
    {
    }
}
