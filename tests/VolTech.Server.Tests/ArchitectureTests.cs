using System.Reflection;
using NetArchTest.Rules;
using VolTech.Server.Abstractions;
using VolTech.Server.Hosting;

namespace VolTech.Server.Tests
{
    /// <summary>
    /// フォルダー(名前空間)の間の依存の向きを確かめる(docs/design/project-structure.md の「2 つに分ける理由」)。
    /// </summary>
    public class ArchitectureTests
    {
        private const string Auth = "VolTech.Server.Auth";
        private const string Status = "VolTech.Server.Status";
        private const string Transaction = "VolTech.Server.Transaction";
        private const string Logging = "VolTech.Server.Logging";
        private const string Hosting = "VolTech.Server.Hosting";
        private const string Internal = "VolTech.Server.Internal";

        // Hosting/ のうち、名前空間が VolTech.Server(ルート)の型
        private const string HostingRootType = "VolTech.Server.ServiceContextExtensions";

        /// <summary>
        /// DB(Npgsql・DbUp)を使うのは Auth/(端末の登録とトークン)と Transaction/ だけ。ほかの型は参照しない。
        /// フォルダーを並べずに「Auth/・Transaction/ 以外のすべて」を対象にし、フォルダーを足したときに漏れないようにする。
        /// </summary>
        [Fact]
        public void OnlyAuthAndTransaction_DependOnDatabase()
        {
            var types = Types.InAssembly(typeof(VoltechServerExtensions).Assembly)
                .That().DoNotResideInNamespace(Auth)
                .And().DoNotResideInNamespace(Transaction);
            Assert.NotEmpty(types.GetTypes());

            var result = types.ShouldNot().HaveDependencyOnAny("Npgsql", "DbUp").GetResult();

            AssertSuccessful(result);
        }

        /// <summary>Internal/ はフィルターどうしで共有する部品なので、ほかのフォルダーに依存しない。</summary>
        [Fact]
        public void Internal_DoesNotDependOnOtherFolders()
        {
            var types = InFolder(Internal);

            var result = types.ShouldNot()
                .HaveDependencyOnAny(Auth, Status, Transaction, Logging, Hosting, HostingRootType)
                .GetResult();

            AssertSuccessful(result);
        }

        /// <summary>Abstractions は Unity と共有する(netstandard2.1)ので、サーバーのコードとサーバー専用の依存を参照しない。</summary>
        [Fact]
        public void Abstractions_DoesNotDependOnServer()
        {
            var types = Types.InAssembly(typeof(ErrorCode).Assembly).That().ResideInNamespace("VolTech.Server.Abstractions");
            Assert.NotEmpty(types.GetTypes());

            var result = types.ShouldNot()
                .HaveDependencyOnAny(
                    Auth, Status, Transaction, Logging, Hosting, Internal, HostingRootType,
                    "Npgsql", "DbUp", "MagicOnion.Server", "Grpc.AspNetCore", "Microsoft.AspNetCore")
                .GetResult();

            AssertSuccessful(result);
        }

        private static PredicateList InFolder(string folder)
        {
            var assembly = typeof(VoltechServerExtensions).Assembly;
            var predicates = Types.InAssembly(assembly).That().ResideInNamespace(folder);
            if (folder == Hosting)
            {
                predicates = predicates.Or().ResideInNamespaceMatching(@"^VolTech\.Server$").And().HaveName("ServiceContextExtensions");
            }

            // 名前空間を書き間違えると、型が 0 個のままルールが通ってしまうので確かめる
            Assert.NotEmpty(predicates.GetTypes());
            return predicates;
        }

        private static void AssertSuccessful(TestResult result)
        {
            Assert.True(result.IsSuccessful, "ルールに違反した型: " + string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
        }
    }
}
