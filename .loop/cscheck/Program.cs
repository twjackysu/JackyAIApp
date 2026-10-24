// 迴圈專用的離線 C# 檢查器（NuGet 連不到、CI 停擺時用）。合併回 master 前整個 .loop/ 刪掉。
// 用 SDK 內附的 Roslyn 與 targeting pack 編譯 server 與 tests 的原始碼；第三方套件沒有，所以會有
// 「找不到型別」之類的雜訊。用法見 .loop/cscheck/README.md：對 master 與 branch 各跑一次再比較，
// 新出現的診斷才是候選錯誤。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

var repo = args[0];
var dotnet = args.Length > 1 ? args[1] : "/usr/lib/dotnet";

var refs = new List<MetadataReference>();
foreach (var pack in new[] { "Microsoft.NETCore.App.Ref", "Microsoft.AspNetCore.App.Ref" })
{
    var packDir = Directory.GetDirectories(Path.Combine(dotnet, "packs", pack)).OrderBy(d => d).Last();
    foreach (var dll in Directory.GetFiles(Path.Combine(packDir, "ref", "net10.0"), "*.dll"))
        refs.Add(MetadataReference.CreateFromFile(dll));
}

// 第三方套件的原始碼（若有 clone）當作參考編譯：它們本身的錯誤不報，只用來讓我們的程式碼有型別可以綁定。
// CSCHECK_REFS="Anthropic=/path/src/Anthropic;xunit.assert=/path/assert.xunit;..."（run.sh 會自動帶上找得到的）
var refSourceParse = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.None,
    preprocessorSymbols: new[] { "NET", "NET8_0_OR_GREATER", "NET9_0_OR_GREATER", "NET10_0_OR_GREATER", "NETCOREAPP",
        "NETCOREAPP3_0_OR_GREATER", "NET6_0_OR_GREATER", "NET7_0_OR_GREATER", "XUNIT_NULLABLE", "XUNIT_SPAN" });
foreach (var spec in (Environment.GetEnvironmentVariable("CSCHECK_REFS") ?? "")
             .Split(';', StringSplitOptions.RemoveEmptyEntries))
{
    var (name, dir) = (spec[..spec.IndexOf('=')], spec[(spec.IndexOf('=') + 1)..]);
    if (!Directory.Exists(dir)) continue;
    var refTrees = Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
        .Where(f => !f.Contains("/bin/") && !f.Contains("/obj/") && !f.Contains("Test"))
        .Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), refSourceParse, path: f))
        .ToList();
    var refCompilation = CSharpCompilation.Create(name, refTrees, refs.ToList(),
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable,
            allowUnsafe: true));
    refs.Add(refCompilation.ToMetadataReference());
    Console.Error.WriteLine($"參考原始碼 {name}: {refTrees.Count} 個檔案");
}

var parse = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Diagnose,
    preprocessorSymbols: new[] { "NET", "NET10_0", "NET10_0_OR_GREATER", "NETCOREAPP", "RELEASE" });

string[] webUsings = { "System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Net.Http",
    "System.Net.Http.Json", "System.Threading", "System.Threading.Tasks", "Microsoft.AspNetCore.Builder",
    "Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Http", "Microsoft.AspNetCore.Routing",
    "Microsoft.Extensions.Configuration", "Microsoft.Extensions.DependencyInjection",
    "Microsoft.Extensions.Hosting", "Microsoft.Extensions.Logging" };
string[] libUsings = { "System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Net.Http",
    "System.Threading", "System.Threading.Tasks", "Xunit" };

IEnumerable<SyntaxTree> Trees(string dir, string[] usings)
{
    var files = Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
        .Where(f => !f.Contains("/bin/") && !f.Contains("/obj/"));
    foreach (var f in files)
        yield return CSharpSyntaxTree.ParseText(File.ReadAllText(f), parse, path: Path.GetRelativePath(repo, f));
    var global = string.Join("\n", usings.Select(u => $"global using global::{u};"))
        + "\n[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"JackyAIApp.Server.Tests\")]";
    yield return CSharpSyntaxTree.ParseText(global, parse, path: "<implicit-usings>");
}

var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
    nullableContextOptions: NullableContextOptions.Enable,
    specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic>
    {
        ["CS1591"] = ReportDiagnostic.Suppress,
        ["CS1573"] = ReportDiagnostic.Suppress,
    });

var server = CSharpCompilation.Create("JackyAIApp.Server",
    Trees(Path.Combine(repo, "JackyAIApp.Server"), webUsings), refs, options);
var tests = CSharpCompilation.Create("JackyAIApp.Server.Tests",
    Trees(Path.Combine(repo, "JackyAIApp.Server.Tests"), libUsings),
    refs.Append(server.ToMetadataReference()), options);

foreach (var c in new[] { server, tests })
{
    foreach (var d in c.GetDiagnostics())
    {
        if (d.Severity < DiagnosticSeverity.Warning) continue;
        var span = d.Location.GetLineSpan();
        // 行號另外印在最後，比較時用前面的欄位（行號會因無關修改而移動）。
        Console.WriteLine($"{span.Path}\t{d.Id}\t{d.GetMessage()}\t{span.StartLinePosition.Line + 1}");
    }
}
