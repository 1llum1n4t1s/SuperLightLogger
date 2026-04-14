using Xunit;

namespace SuperLightLogger.Tests.Helpers;

/// <summary>
/// <see cref="LogManager"/> の静的状態 (<c>_factory</c>, <c>_warningEmitted</c>) を
/// 触るテストクラスを同じコレクションに入れて直列実行させるための定義。
/// xunit.v3 は既定でクラス毎に並列実行するため、これがないと
/// 別クラスのテストが static _factory を差し替えて Shutdown 系テストが偶発的に失敗する。
/// </summary>
[CollectionDefinition(Name)]
public sealed class LogManagerCollection
{
    public const string Name = "LogManager static state";
}
