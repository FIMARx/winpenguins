using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using WinPenguins.Assets;
using WinPenguins.Config;
using WinPenguins.Engine;
using WinPenguins.Platform;
using WinPenguins.UI;
using Xunit;

namespace WinPenguins.Tests;

public sealed class SourceAndContractGuards
{
    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WinPenguins.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing WinPenguins.slnx.");
    }

    private static IReadOnlyList<(string RelativePath, SyntaxTree Tree, string Text)> LoadSourceTrees()
    {
        string srcRoot = Path.Combine(FindRepoRoot(), "src", "WinPenguins");
        string[] files = Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                        !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var result = new List<(string, SyntaxTree, string)>(files.Length);
        foreach (string path in files)
        {
            string text = File.ReadAllText(path);
            string rel = Path.GetRelativePath(srcRoot, path).Replace('\\', '/');
            result.Add((rel, CSharpSyntaxTree.ParseText(text, path: rel), text));
        }

        return result;
    }

    [Fact]
    public void SourceTree_DiscoveredExpectedProductionFiles()
    {
        IReadOnlyList<(string RelativePath, SyntaxTree Tree, string Text)> sources = LoadSourceTrees();
        Assert.True(sources.Count >= 20, $"Expected >= 20 production C# files, found {sources.Count}.");
        Assert.Contains(sources, s => s.RelativePath.Equals("Program.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(sources, s => s.RelativePath.Equals("UI/OptionsForm.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Guard_NoThreadSleepInsideUiLayer()
    {
        IReadOnlyList<(string RelativePath, SyntaxTree Tree, string Text)> sources = LoadSourceTrees();
        var violations = new List<string>();

        foreach ((string rel, SyntaxTree tree, _) in sources.Where(s => s.RelativePath.StartsWith("UI/", StringComparison.OrdinalIgnoreCase)))
        {
            violations.AddRange(FindThreadSleepCalls(rel, tree));
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void Guard_NoDiscardedProcessStartHandles()
    {
        IReadOnlyList<(string RelativePath, SyntaxTree Tree, string Text)> sources = LoadSourceTrees();
        var violations = new List<string>();

        foreach ((string rel, SyntaxTree tree, _) in sources)
        {
            violations.AddRange(FindDiscardedProcessStartCalls(rel, tree));
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void Guard_EveryFormClassAppliesFluentWindowChrome()
    {
        IReadOnlyList<(string RelativePath, SyntaxTree Tree, string Text)> sources = LoadSourceTrees();
        var missingChrome = new List<string>();

        foreach ((string rel, SyntaxTree tree, string text) in sources)
        {
            SyntaxNode root = tree.GetRoot();
            foreach (ClassDeclarationSyntax cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                bool inheritsForm = cls.BaseList?.Types.Any(t =>
                    string.Equals(t.Type.ToString(), "Form", StringComparison.Ordinal)) ?? false;
                if (!inheritsForm)
                {
                    continue;
                }

                if (!text.Contains("ApplyWindowChrome", StringComparison.Ordinal))
                {
                    missingChrome.Add($"{rel}:{cls.Identifier.Text}");
                }
            }
        }

        Assert.Empty(missingChrome);
    }

    [Fact]
    public void GuardSelfTest_RulesActuallyCatchInjectedBadCode()
    {
        // Analogous to DigiKaveri ui-rules self-test: ensure AST rules aren't silent no-ops.
        SyntaxTree badUiTree = CSharpSyntaxTree.ParseText("""
            using System.Threading;
            class FakeUiForm { void Bad() { Thread.Sleep(50); } }
            """);
        Assert.NotEmpty(FindThreadSleepCalls("UI/FakeUiForm.cs", badUiTree));

        SyntaxTree badProcessTree = CSharpSyntaxTree.ParseText("""
            using System.Diagnostics;
            class FakeLauncher { void Bad() { Process.Start("notepad.exe"); } }
            """);
        Assert.NotEmpty(FindDiscardedProcessStartCalls("FakeLauncher.cs", badProcessTree));

        SyntaxTree cleanProcessTree = CSharpSyntaxTree.ParseText("""
            using System.Diagnostics;
            class CleanLauncher { void Good() { using Process? p = Process.Start("notepad.exe"); } }
            """);
        Assert.Empty(FindDiscardedProcessStartCalls("CleanLauncher.cs", cleanProcessTree));
    }

    [Fact]
    public void Contract_AllEmbeddedSpritesSoundsIconAndLicenseResourcesExist()
    {
        Assembly asm = typeof(AppSettings).Assembly;
        HashSet<string> resources = new(asm.GetManifestResourceNames(), StringComparer.Ordinal);

        Assert.Contains("WinPenguins.Assets.winpenguins.ico", resources);
        Assert.Contains("WinPenguins.Assets.COPYING.txt", resources);

        foreach (string sound in new[] { "boom", "eep", "quack" })
        {
            Assert.Contains($"WinPenguins.Assets.{sound}.wav", resources);
        }

        foreach (AnimationSpec spec in AnimationCatalogSpecs.All.Values)
        {
            Assert.Contains($"WinPenguins.Assets.{spec.ResourcePrefix}.bmp", resources);
            Assert.Contains($"WinPenguins.Assets.{spec.ResourcePrefix}Mask.bmp", resources);
        }
    }

    [Fact]
    public void Contract_AllCrewSubTypesAreWiredInSettingsAndRenderer()
    {
        IReadOnlyList<(string RelativePath, SyntaxTree Tree, string Text)> sources = LoadSourceTrees();
        string dcompText = sources.First(s => s.RelativePath.Equals("Platform/DCompOverlayWindow.cs", StringComparison.OrdinalIgnoreCase)).Text;
        string simText = sources.First(s => s.RelativePath.Equals("Engine/PenguinSimulation.cs", StringComparison.OrdinalIgnoreCase)).Text;
        var settings = new AppSettings();

        foreach (PenguinSubType subType in Enum.GetValues<PenguinSubType>())
        {
            string member = $"PenguinSubType.{subType}";
            Assert.Contains(member, simText, StringComparison.Ordinal);

            if (subType != PenguinSubType.Unspecified && subType != PenguinSubType.Normal)
            {
                if (subType == PenguinSubType.Santa)
                {
                    Assert.Contains("SantaWalker", simText, StringComparison.Ordinal);
                }
                else
                {
                    Assert.Contains(member, dcompText, StringComparison.Ordinal);
                    Assert.Contains(subType, settings.GetEnabledSpecialCrew());
                }
            }
        }

        string toysRendererText = sources.First(s => s.RelativePath.Equals("Platform/DCompOverlayWindow.cs", StringComparison.OrdinalIgnoreCase)).Text;
        foreach (ToyKind toy in Enum.GetValues<ToyKind>())
        {
            Assert.Contains($"ToyKind.{toy}", toysRendererText, StringComparison.Ordinal);
        }

        foreach (PenguinEmote emote in Enum.GetValues<PenguinEmote>())
        {
            if (emote != PenguinEmote.None)
            {
                Assert.Contains($"PenguinEmote.{emote}", dcompText, StringComparison.Ordinal);
            }
        }

        foreach (PenguinTemperament temperament in Enum.GetValues<PenguinTemperament>())
        {
            Assert.Contains($"PenguinTemperament.{temperament}", simText, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(PenguinPersonality.GetTemperamentLabel(temperament)));
        }

        foreach (PenguinQuirk quirk in Enum.GetValues<PenguinQuirk>())
        {
            Assert.False(string.IsNullOrWhiteSpace(PenguinPersonality.GetQuirkLabel(quirk)));
        }
    }

    private static List<string> FindThreadSleepCalls(string relPath, SyntaxTree tree)
    {
        var hits = new List<string>();
        foreach (InvocationExpressionSyntax inv in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (inv.Expression is MemberAccessExpressionSyntax ma &&
                string.Equals(ma.Name.Identifier.Text, "Sleep", StringComparison.Ordinal) &&
                ma.Expression.ToString().EndsWith("Thread", StringComparison.Ordinal))
            {
                int line = inv.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                hits.Add($"{relPath}:{line}  {inv}");
            }
        }

        return hits;
    }

    private static List<string> FindDiscardedProcessStartCalls(string relPath, SyntaxTree tree)
    {
        var hits = new List<string>();
        foreach (ExpressionStatementSyntax stmt in tree.GetRoot().DescendantNodes().OfType<ExpressionStatementSyntax>())
        {
            if (stmt.Expression is InvocationExpressionSyntax inv &&
                inv.Expression is MemberAccessExpressionSyntax ma &&
                string.Equals(ma.Name.Identifier.Text, "Start", StringComparison.Ordinal) &&
                ma.Expression.ToString().EndsWith("Process", StringComparison.Ordinal))
            {
                int line = stmt.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                hits.Add($"{relPath}:{line}  {stmt}");
            }
        }

        return hits;
    }
}
