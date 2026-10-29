using System.Runtime.CompilerServices;

// パッケージの中のアセンブリにだけ internal を見せる。アプリに見せるものは public にする
[assembly: InternalsVisibleTo("VolTech.MagicOnion.Client.Tests.EditMode")]
