using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using MessagePack;
using MessagePack.Resolvers;

namespace VolTech.Client.Editor
{
    /// <summary>
    /// メッセージを、項目名の付いた JSON にする。
    /// </summary>
    /// <remarks>
    /// MessagePack は [Key(0)] の型を配列で書くので、そのまま JSON にすると項目名が消える。
    /// そこでリフレクションでメンバーをたどって、辞書・リスト・葉の木に変え、木を MessagePack にして ConvertToJson に通す。
    /// JSON のテキスト(エスケープなど)は MessagePack に任せる。
    /// 使っていない型は例外にする。受け取った側が、項目名の無い配列の表示に切り替える。
    /// Box と DynamicArgumentTuple は MagicOnion の内部の包みなので、コンパイル時に型へ依存せず、名前で判定する。
    /// </remarks>
    internal static class ApiTraceJson
    {
        private const string BoxTypeName = "MagicOnion.Internal.Box`1";
        private const string TupleTypeNamePrefix = "MagicOnion.DynamicArgumentTuple`";

        /// <summary>
        /// 値を JSON にする。
        /// </summary>
        /// <exception cref="NotSupportedException">対応していない型が含まれているとき。</exception>
        public static string Format(object value)
        {
            var bytes = MessagePackSerializer.Serialize(ToTree(value), PrimitiveObjectResolver.Options);
            return MessagePackSerializer.ConvertToJson(new ReadOnlyMemory<byte>(bytes));
        }

        private static object ToTree(object value)
        {
            switch (value)
            {
                case null:
                case Nil:
                    return null;
                case string or bool or sbyte or byte or short or ushort or int or uint or long or ulong:
                    return value;
                case float number:
                    return float.IsNaN(number) || float.IsInfinity(number) ? number.ToString(CultureInfo.InvariantCulture) : value;
                case double number:
                    return double.IsNaN(number) || double.IsInfinity(number) ? number.ToString(CultureInfo.InvariantCulture) : value;
                case Enum:
                    return value.ToString();
                case DateTime time:
                    return time.ToString("o", CultureInfo.InvariantCulture);
                case byte[] bytes:
                    return Convert.ToBase64String(bytes);
            }

            var type = value.GetType();
            var fullName = type.FullName ?? "";
            if (fullName.StartsWith(BoxTypeName, StringComparison.Ordinal))
            {
                return ToTree(type.GetField("Value").GetValue(value));
            }

            if (fullName.StartsWith(TupleTypeNamePrefix, StringComparison.Ordinal))
            {
                return TupleToTree(type, value);
            }

            if (type.GetCustomAttribute<MessagePackObjectAttribute>() != null)
            {
                return ObjectToTree(type, value);
            }

            switch (value)
            {
                case IDictionary dictionary:
                    return DictionaryToTree(dictionary);
                case IEnumerable items:
                    return items.Cast<object>().Select(ToTree).ToList();
                default:
                    throw new NotSupportedException($"{type} は JSON にできない。");
            }
        }

        private static List<object> TupleToTree(Type type, object value)
        {
            var items = new List<object>();
            var count = type.GetGenericArguments().Length;
            for (var index = 1; index <= count; index++)
            {
                items.Add(ToTree(type.GetField($"Item{index}").GetValue(value)));
            }

            return items;
        }

        private static Dictionary<string, object> DictionaryToTree(IDictionary dictionary)
        {
            var tree = new Dictionary<string, object>();
            foreach (DictionaryEntry entry in dictionary)
            {
                tree[Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? ""] = ToTree(entry.Value);
            }

            return tree;
        }

        // 既定値の項目も出す。送った・受け取ったことが分かるようにするため
        private static Dictionary<string, object> ObjectToTree(Type type, object value)
        {
            var members = KeyedMembers(type);
            if (members.Count == 0)
            {
                throw new NotSupportedException($"{type} に int の [Key] が無い。");
            }

            var tree = new Dictionary<string, object>();
            foreach (var member in members)
            {
                tree[member.Name] = ToTree(member is PropertyInfo property ? property.GetValue(value) : ((FieldInfo)member).GetValue(value));
            }

            return tree;
        }

        // int の [Key] が付いたプロパティとフィールドを、基底クラスの分も含めて、キーの小さい順に返す
        private static List<MemberInfo> KeyedMembers(Type type)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var members = new List<(int Key, MemberInfo Member)>();
            for (var current = type; current != null; current = current.BaseType)
            {
                foreach (var member in current.GetMembers(flags))
                {
                    if (member is PropertyInfo or FieldInfo && member.GetCustomAttribute<KeyAttribute>()?.IntKey is { } key)
                    {
                        members.Add((key, member));
                    }
                }
            }

            return members.OrderBy(pair => pair.Key).Select(pair => pair.Member).ToList();
        }
    }
}
