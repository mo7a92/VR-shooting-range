// StubGen: turns C# sources into declaration-only "reference" sources (bodies replaced with `throw null`),
// and prunes members that fail to compile (error-driven) so a reference assembly can be produced from
// UnityCsReference without Unity's private/third-party build dependencies. Cloud validation tool only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

static class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0) { Console.Error.WriteLine("usage: stubgen gen --out DIR --root ROOT --defines A;B @files.txt | prune --log LOG --root STUBDIR"); return 2; }
        return args[0] == "gen" ? Gen(args) : args[0] == "prune" ? Prune(args) : 2;
    }

    static string Arg(string[] a, string name, string def = null) { int i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : def; }

    static int Gen(string[] args)
    {
        var outDir = Arg(args, "--out"); var root = Path.GetFullPath(Arg(args, "--root"));
        var defines = (Arg(args, "--defines", "") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        var listFile = args.First(x => x.StartsWith("@")).Substring(1);
        var files = File.ReadAllLines(listFile).Where(l => l.Trim().Length > 0).ToList();
        var parse = new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: defines);
        int n = 0, failed = 0;
        foreach (var f in files)
        {
            try
            {
                var text = File.ReadAllText(f);
                var tree = CSharpSyntaxTree.ParseText(text, parse, f);
                var rootNode = tree.GetCompilationUnitRoot();
                var stub = (CompilationUnitSyntax)new StubRewriter().Visit(rootNode);
                stub = stub.NormalizeWhitespace();
                var rel = Path.GetRelativePath(root, Path.GetFullPath(f));
                var dst = Path.Combine(outDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                var lines = stub.ToFullString().Split('\n').Where(l => !l.TrimStart().StartsWith("#"));
                File.WriteAllText(dst, string.Join("\n", lines));
                n++;
            }
            catch (Exception e) { failed++; Console.Error.WriteLine($"stub failed: {f}: {e.Message}"); }
        }
        Console.WriteLine($"stubbed {n} files, {failed} failed");
        return 0;
    }

    class StubRewriter : CSharpSyntaxRewriter
    {
        static readonly HashSet<string> KeepAttrs = new HashSet<string> { "Obsolete", "ObsoleteAttribute", "Flags", "FlagsAttribute", "Serializable", "SerializableAttribute",
            "StructLayout", "StructLayoutAttribute", "FieldOffset", "FieldOffsetAttribute", "AttributeUsage", "AttributeUsageAttribute", "InternalsVisibleTo", "InternalsVisibleToAttribute",
            "DefaultMember", "DefaultMemberAttribute", "Extension", "ExtensionAttribute", "NativeType", "NativeTypeAttribute", "RequiredByNativeCode", "RequiredByNativeCodeAttribute",
            "UsedByNativeCode", "UsedByNativeCodeAttribute", "NativeHeader", "NativeHeaderAttribute", "NativeClass", "NativeClassAttribute", "DebuggerDisplay", "DebuggerDisplayAttribute",
            "CLSCompliant", "CLSCompliantAttribute", "ComVisible", "ComVisibleAttribute", "Guid", "GuidAttribute", "DllImport", "DllImportAttribute", "MarshalAs", "MarshalAsAttribute" };

        public StubRewriter() : base(visitIntoStructuredTrivia: false) { }

        public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia)
        {
            if (trivia.IsKind(SyntaxKind.WhitespaceTrivia) || trivia.IsKind(SyntaxKind.EndOfLineTrivia)) return trivia;
            return default;
        }

        static BlockSyntax ThrowBlock() => SyntaxFactory.Block(SyntaxFactory.ThrowStatement(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)));
        static BlockSyntax EmptyBlock() => SyntaxFactory.Block();

        public override SyntaxNode VisitAttributeList(AttributeListSyntax node)
        {
            var kept = node.Attributes.Where(a => KeepAttrs.Contains(a.Name.ToString().Split('.').Last())).ToList();
            if (kept.Count == 0) return null;
            return node.WithAttributes(SyntaxFactory.SeparatedList(kept));
        }

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            node = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node);
            if (node.Body == null && node.ExpressionBody == null) return node; // abstract/extern/interface/partial
            return node.WithBody(ThrowBlock()).WithExpressionBody(null).WithSemicolonToken(default);
        }
        public override SyntaxNode VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            node = (ConstructorDeclarationSyntax)base.VisitConstructorDeclaration(node);
            if (node.Body == null && node.ExpressionBody == null) return node;
            var init = node.Initializer;
            if (init != null)
            {
                var paramNames = new HashSet<string>(node.ParameterList.Parameters.Select(p => p.Identifier.Text));
                var args = init.ArgumentList.Arguments.Select(a =>
                    (a.Expression is IdentifierNameSyntax id && paramNames.Contains(id.Identifier.Text)) || a.Expression is LiteralExpressionSyntax
                        ? a.WithNameColon(null)
                        : SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression)).WithRefKindKeyword(a.RefKindKeyword));
                init = init.WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(args)));
            }
            if (init != null && init.IsKind(SyntaxKind.ThisConstructorInitializer)) init = null; // chaining is irrelevant for stubs
            return node.WithInitializer(init).WithBody(ThrowBlock()).WithExpressionBody(null).WithSemicolonToken(default);
        }
        public override SyntaxNode VisitDestructorDeclaration(DestructorDeclarationSyntax node)
        {
            node = (DestructorDeclarationSyntax)base.VisitDestructorDeclaration(node);
            return node.WithBody(EmptyBlock()).WithExpressionBody(null).WithSemicolonToken(default);
        }
        public override SyntaxNode VisitOperatorDeclaration(OperatorDeclarationSyntax node)
        {
            node = (OperatorDeclarationSyntax)base.VisitOperatorDeclaration(node);
            if (node.Body == null && node.ExpressionBody == null) return node;
            return node.WithBody(ThrowBlock()).WithExpressionBody(null).WithSemicolonToken(default);
        }
        public override SyntaxNode VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
        {
            node = (ConversionOperatorDeclarationSyntax)base.VisitConversionOperatorDeclaration(node);
            if (node.Body == null && node.ExpressionBody == null) return node;
            return node.WithBody(ThrowBlock()).WithExpressionBody(null).WithSemicolonToken(default);
        }
        public override SyntaxNode VisitAccessorDeclaration(AccessorDeclarationSyntax node)
        {
            node = (AccessorDeclarationSyntax)base.VisitAccessorDeclaration(node);
            if (node.Body == null && node.ExpressionBody == null) return node; // auto accessor
            bool isGet = node.IsKind(SyntaxKind.GetAccessorDeclaration);
            return node.WithBody(isGet ? ThrowBlock() : EmptyBlock()).WithExpressionBody(null).WithSemicolonToken(default);
        }
        public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            node = (PropertyDeclarationSyntax)base.VisitPropertyDeclaration(node);
            if (node.ExpressionBody != null)
                return node.WithExpressionBody(SyntaxFactory.ArrowExpressionClause(SyntaxFactory.ThrowExpression(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression))));
            if (node.Initializer != null) return node.WithInitializer(null).WithSemicolonToken(default);
            return node;
        }
        public override SyntaxNode VisitIndexerDeclaration(IndexerDeclarationSyntax node)
        {
            node = (IndexerDeclarationSyntax)base.VisitIndexerDeclaration(node);
            if (node.ExpressionBody != null)
                return node.WithExpressionBody(SyntaxFactory.ArrowExpressionClause(SyntaxFactory.ThrowExpression(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression))));
            return node;
        }
        public override SyntaxNode VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            node = (VariableDeclaratorSyntax)base.VisitVariableDeclarator(node);
            var field = node.Parent?.Parent as FieldDeclarationSyntax;
            if (field != null && field.Modifiers.Any(SyntaxKind.ConstKeyword)) return node;
            var ev = node.Parent?.Parent as EventFieldDeclarationSyntax;
            if (node.Initializer != null && (field != null || ev != null)) return node.WithInitializer(null);
            return node;
        }
        public override SyntaxNode VisitParameter(ParameterSyntax node)
        {
            node = (ParameterSyntax)base.VisitParameter(node);
            if (node.Default != null)
            {
                var v = node.Default.Value;
                bool simple = v is LiteralExpressionSyntax || v is PrefixUnaryExpressionSyntax || v is DefaultExpressionSyntax || v is MemberAccessExpressionSyntax || v is IdentifierNameSyntax || v is CastExpressionSyntax;
                if (!simple) return node.WithDefault(SyntaxFactory.EqualsValueClause(SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression)));
            }
            return node;
        }
        public override SyntaxNode VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) => null;
    }

    // ---------- prune ----------
    static int Prune(string[] args)
    {
        var log = Arg(args, "--log"); var root = Path.GetFullPath(Arg(args, "--root"));
        var rx = new Regex(@"^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\): error (?<code>CS\d+): (?<msg>.*)$");
        var byFile = new Dictionary<string, List<(int line, int col, string code, string msg)>>();
        foreach (var l in File.ReadAllLines(log))
        {
            var m = rx.Match(l.Trim()); if (!m.Success) continue;
            var f = Path.GetFullPath(m.Groups["file"].Value);
            if (!f.StartsWith(root)) continue;
            if (!byFile.TryGetValue(f, out var list)) byFile[f] = list = new List<(int, int, string, string)>();
            list.Add((int.Parse(m.Groups["line"].Value), int.Parse(m.Groups["col"].Value), m.Groups["code"].Value, m.Groups["msg"].Value));
        }
        int removed = 0, filesTouched = 0;
        foreach (var kv in byFile)
        {
            var text = File.ReadAllText(kv.Key);
            var tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Preview), kv.Key);
            var rootNode = tree.GetCompilationUnitRoot();
            var toRemove = new HashSet<SyntaxNode>();
            foreach (var e in kv.Value.DistinctBy(x => (x.line, x.col)))
            {
                var pos = tree.GetText().Lines[Math.Min(e.line - 1, tree.GetText().Lines.Count - 1)].Start + Math.Max(0, e.col - 1);
                var tok = rootNode.FindToken(pos);
                var node = PickRemovable(tok.Parent, e.code);
                if (node != null) toRemove.Add(node);
            }
            if (toRemove.Count == 0) continue;
            // drop nodes nested inside other removed nodes
            var final = toRemove.Where(n => !toRemove.Any(o => o != n && o.Span.Contains(n.Span) && o.Span != n.Span)).ToList();
            var newRoot = rootNode.RemoveNodes(final, SyntaxRemoveOptions.KeepNoTrivia);
            newRoot = FixEmptyBaseLists(newRoot);
            File.WriteAllText(kv.Key, newRoot.NormalizeWhitespace().ToFullString());
            removed += final.Count; filesTouched++;
        }
        Console.WriteLine($"pruned {removed} nodes in {filesTouched} files");
        return removed > 0 ? 0 : 3;
    }

    static SyntaxNode PickRemovable(SyntaxNode n, string code)
    {
        // Walk up to the smallest "removable" construct containing the error.
        for (var cur = n; cur != null; cur = cur.Parent)
        {
            switch (cur)
            {
                case ConstructorInitializerSyntax ci: return ci; // drop just the base(...) call first
                case UsingDirectiveSyntax u: return u;
                case AttributeSyntax a: return a.Parent is AttributeListSyntax al && al.Attributes.Count == 1 ? al : a;
                case SimpleBaseTypeSyntax bt: return bt;
                case PrimaryConstructorBaseTypeSyntax pbt: return pbt;
                case TypeParameterConstraintClauseSyntax tc: return tc.Parent; // remove whole type/method
                case EnumMemberDeclarationSyntax em: return em;
                case BaseTypeDeclarationSyntax td:
                    // error anchored on the type header itself (e.g. unimplemented interface member, duplicate type)
                    return td;
                case DelegateDeclarationSyntax dd: return dd;
                case MemberDeclarationSyntax md when !(md is BaseNamespaceDeclarationSyntax): return md;
            }
        }
        return null;
    }

    static CompilationUnitSyntax FixEmptyBaseLists(CompilationUnitSyntax root)
    {
        var empties = root.DescendantNodes().OfType<BaseListSyntax>().Where(b => b.Types.Count == 0).ToList();
        if (empties.Count == 0) return root;
        return root.RemoveNodes(empties, SyntaxRemoveOptions.KeepNoTrivia);
    }
}
