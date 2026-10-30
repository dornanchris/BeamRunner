// UccTool - Roslyn host used by tools/unity-compile-check.
//
//   UccTool compile <plan.json> <result.json>
//       Runs a sequence of C# compilations described by plan.json (sources, references, defines, ...).
//       Later compilations may reference earlier ones by name (in-memory CompilationReference, so a
//       downstream stage is still checked even if an upstream one has errors). Reports ALL diagnostics
//       (Compilation.GetDiagnostics, i.e. method bodies are bound even when declarations have errors).
//       For "missing member/type" errors it records the receiver type (+ base types) so the Python
//       driver can check the UnityCsReference API index (the project's Unity version, e.g. 6000.3.25f1)
//       and flag APIs that only exist in that version. Optionally does a semantic "Unity 6 API" pass:
//       every resolved UnityEngine/UnityEditor symbol is looked up in that index to report [Obsolete]
//       usage and APIs missing from that version.
//
//   UccTool index <UnityCsReference-root> <out.tsv>
//       Syntax-only scan of UnityCsReference producing an index of public/protected types & members
//       with their [Obsolete] state.
//
//   UccTool dump-refs <out.tsv> <dll>...
//       Dumps the public API (type/member names) of reference assemblies.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace UccTool;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 1) { Usage(); return 2; }
        try
        {
            switch (args[0])
            {
                case "compile": return Compiler.Run(args[1], args[2]);
                case "index": return CsRefIndexer.Run(args[1], args[2]);
                case "dump-refs": return RefDumper.Run(args[1], args.Skip(2).ToArray());
                default: Usage(); return 2;
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("UccTool failed: " + e);
            return 3;
        }
    }

    static void Usage() => Console.Error.WriteLine("usage: UccTool compile <plan.json> <result.json> | index <csref-root> <out.tsv> | dump-refs <out.tsv> <dll>...");
}

// ------------------------------------------------------------------------------------------------
// compile
// ------------------------------------------------------------------------------------------------

public sealed class Plan
{
    public List<CompileSpec> compilations { get; set; } = new();
    public string unity6Index { get; set; }
    public string unity6Version { get; set; } = "6000.x";
    public List<string> auditAssemblyPrefixes { get; set; } = new() { "UnityEngine", "UnityEditor" };
}

public sealed class CompileSpec
{
    public string name { get; set; }
    public string assemblyName { get; set; }
    public List<string> sources { get; set; } = new();
    public List<string> references { get; set; } = new();
    public List<string> compilationReferences { get; set; } = new();
    public List<string> defines { get; set; } = new();
    public bool allowUnsafe { get; set; }
    public string langVersion { get; set; } = "9.0";
    public string outputPath { get; set; }
    public List<string> nowarn { get; set; } = new();
    public bool unity6Audit { get; set; }
    // Package sources newer than the engine reference assemblies: replace the bodies of members whose bodies do not
    // compile with `throw null` (see BodyStripper). The public API surface stays exactly the real source's.
    public bool stripBodiesOnError { get; set; }
}

public sealed class DiagInfo
{
    public string id { get; set; }
    public string severity { get; set; }
    public string file { get; set; }
    public int line { get; set; }
    public int col { get; set; }
    public string message { get; set; }
    public bool declarationLevel { get; set; }
    // Filled for missing member / missing type errors.
    public string missingMember { get; set; }
    public List<string> receiverTypes { get; set; }
    public string missingType { get; set; }
    public List<string> candidateNamespaces { get; set; }
}

public sealed class ApiUse
{
    public string kind { get; set; }      // "obsolete" | "missing"
    public string api { get; set; }       // Namespace.Type.Member
    public string file { get; set; }
    public int line { get; set; }
    public int col { get; set; }
    public string message { get; set; }
    public bool error { get; set; }
}

public sealed class CompileResult
{
    public string name { get; set; }
    public bool success { get; set; }
    public bool emitted { get; set; }
    public long milliseconds { get; set; }
    public int sourceCount { get; set; }
    public List<DiagInfo> diagnostics { get; set; } = new();
    public List<ApiUse> unity6 { get; set; } = new();
    public List<string> stripped { get; set; } = new();
    public string fatal { get; set; }
}

public static class Compiler
{
    static readonly Dictionary<string, MetadataReference> s_RefCache = new(StringComparer.Ordinal);

    public static int Run(string planPath, string resultPath)
    {
        var plan = JsonSerializer.Deserialize<Plan>(File.ReadAllText(planPath));
        Unity6Index index = null;
        if (!string.IsNullOrEmpty(plan.unity6Index) && File.Exists(plan.unity6Index))
            index = Unity6Index.Load(plan.unity6Index, plan.unity6Version);

        var compilations = new Dictionary<string, CSharpCompilation>(StringComparer.Ordinal);
        var results = new List<CompileResult>();
        foreach (var spec in plan.compilations)
        {
            var sw = Stopwatch.StartNew();
            var res = new CompileResult { name = spec.name, sourceCount = spec.sources.Count };
            try
            {
                var comp = Build(spec, compilations, res);
                if (comp != null && spec.stripBodiesOnError)
                    comp = BodyStripper.Strip(comp, res.stripped);
                if (comp != null)
                {
                    compilations[spec.name] = comp;
                    var diags = comp.GetDiagnostics();
                    var declDiags = new HashSet<Diagnostic>(comp.GetDeclarationDiagnostics());
                    foreach (var d in diags)
                    {
                        if (d.Severity != DiagnosticSeverity.Error && d.Severity != DiagnosticSeverity.Warning) continue;
                        if (d.IsSuppressed) continue;
                        res.diagnostics.Add(Describe(comp, d, declDiags.Contains(d)));
                    }
                    res.success = !res.diagnostics.Any(x => x.severity == "error");
                    if (res.success && !string.IsNullOrEmpty(spec.outputPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(spec.outputPath)));
                        var er = comp.Emit(spec.outputPath);
                        res.emitted = er.Success;
                        if (!er.Success)
                        {
                            res.success = false;
                            foreach (var d in er.Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error))
                                res.diagnostics.Add(Describe(comp, d, false));
                        }
                    }
                    if (spec.unity6Audit && index != null)
                        res.unity6 = Unity6Audit(comp, index, plan.auditAssemblyPrefixes);
                }
            }
            catch (Exception e)
            {
                res.success = false;
                res.fatal = e.ToString();
            }
            res.milliseconds = sw.ElapsedMilliseconds;
            results.Add(res);
            Console.Error.WriteLine($"[ucctool] {spec.name}: {(res.success ? "ok" : "FAILED")} ({res.sourceCount} files, {res.diagnostics.Count(x => x.severity == "error")} errors, {res.diagnostics.Count(x => x.severity == "warning")} warnings, {res.milliseconds} ms)");
        }
        File.WriteAllText(resultPath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    static MetadataReference GetRef(string path)
    {
        var full = Path.GetFullPath(path);
        if (!s_RefCache.TryGetValue(full, out var r))
        {
            r = MetadataReference.CreateFromFile(full);
            s_RefCache[full] = r;
        }
        return r;
    }

    static CSharpCompilation Build(CompileSpec spec, Dictionary<string, CSharpCompilation> previous, CompileResult res)
    {
        if (!LanguageVersionFacts.TryParse(spec.langVersion ?? "9.0", out var lv)) lv = LanguageVersion.CSharp9;
        var parseOptions = new CSharpParseOptions(lv, DocumentationMode.None, SourceCodeKind.Regular, spec.defines.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
        var trees = new SyntaxTree[spec.sources.Count];
        Parallel.For(0, spec.sources.Count, i =>
        {
            var path = spec.sources[i];
            using var fs = File.OpenRead(path);
            var text = SourceText.From(fs, Encoding.UTF8, SourceHashAlgorithm.Sha256, throwIfBinaryDetected: false, canBeEmbedded: false);
            trees[i] = CSharpSyntaxTree.ParseText(text, parseOptions, path);
        });

        var refs = new List<MetadataReference>();
        foreach (var r in spec.references) refs.Add(GetRef(r));
        foreach (var cr in spec.compilationReferences)
        {
            if (previous.TryGetValue(cr, out var c)) refs.Add(c.ToMetadataReference());
            else throw new InvalidOperationException($"compilation reference '{cr}' not found (must appear earlier in the plan)");
        }

        var specific = new Dictionary<string, ReportDiagnostic>();
        foreach (var nw in spec.nowarn)
        {
            var id = nw.Trim();
            if (id.Length == 0) continue;
            if (char.IsDigit(id[0])) id = "CS" + id.PadLeft(4, '0');
            specific[id] = ReportDiagnostic.Suppress;
        }

        var options = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: spec.allowUnsafe,
                optimizationLevel: OptimizationLevel.Debug,
                nullableContextOptions: NullableContextOptions.Disable,
                warningLevel: 4,
                deterministic: true,
                concurrentBuild: true,
                specificDiagnosticOptions: specific)
            // Unity's reference assemblies reference mscorlib 2.0.0.0 - allow desktop framework unification like csc does.
            .WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default);

        return CSharpCompilation.Create(spec.assemblyName ?? spec.name, trees, refs, options);
    }

    static DiagInfo Describe(CSharpCompilation comp, Diagnostic d, bool decl)
    {
        var info = new DiagInfo
        {
            id = d.Id,
            severity = d.Severity == DiagnosticSeverity.Error ? "error" : "warning",
            message = d.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
            declarationLevel = decl,
        };
        if (d.Location.IsInSource)
        {
            var span = d.Location.GetMappedLineSpan();
            info.file = span.Path;
            info.line = span.StartLinePosition.Line + 1;
            info.col = span.StartLinePosition.Character + 1;
            if (d.Severity == DiagnosticSeverity.Error)
            {
                try { AnalyzeMissing(comp, d, info); } catch { /* best effort */ }
            }
        }
        return info;
    }

    static readonly HashSet<string> s_MissingMemberIds = new() { "CS1061", "CS0117", "CS0115", "CS1929" };
    static readonly HashSet<string> s_MissingTypeIds = new() { "CS0246", "CS0234", "CS0103", "CS0426" };

    static void AnalyzeMissing(CSharpCompilation comp, Diagnostic d, DiagInfo info)
    {
        if (!s_MissingMemberIds.Contains(d.Id) && !s_MissingTypeIds.Contains(d.Id)) return;
        var tree = d.Location.SourceTree;
        var root = tree.GetRoot();
        var node = root.FindNode(d.Location.SourceSpan, getInnermostNodeForTie: true);
        var model = comp.GetSemanticModel(tree);

        if (s_MissingMemberIds.Contains(d.Id))
        {
            ExpressionSyntax receiver = null;
            string member = null;
            SyntaxNode n = node;
            while (n != null && receiver == null)
            {
                switch (n)
                {
                    case MemberAccessExpressionSyntax ma:
                        receiver = ma.Expression; member = ma.Name.Identifier.Text; break;
                    case MemberBindingExpressionSyntax mb:
                        var cond = mb.FirstAncestorOrSelf<ConditionalAccessExpressionSyntax>();
                        receiver = cond?.Expression; member = mb.Name.Identifier.Text; break;
                    case QualifiedNameSyntax qn:
                        receiver = qn.Left; member = qn.Right.Identifier.Text; break;
                    case MethodDeclarationSyntax md when d.Id == "CS0115":
                        member = md.Identifier.Text;
                        var sym = model.GetDeclaredSymbol(md);
                        if (sym?.ContainingType?.BaseType != null)
                        {
                            info.missingMember = member;
                            info.receiverTypes = TypeChain(sym.ContainingType.BaseType);
                        }
                        return;
                    case PropertyDeclarationSyntax pd when d.Id == "CS0115":
                        var psym = model.GetDeclaredSymbol(pd);
                        if (psym?.ContainingType?.BaseType != null)
                        {
                            info.missingMember = pd.Identifier.Text;
                            info.receiverTypes = TypeChain(psym.ContainingType.BaseType);
                        }
                        return;
                    case StatementSyntax:
                    case MemberDeclarationSyntax:
                        n = null; continue;
                }
                n = n?.Parent;
            }
            if (receiver == null || member == null) return;
            ITypeSymbol t = model.GetTypeInfo(receiver).Type;
            if (t == null || t.TypeKind == TypeKind.Error)
                t = model.GetSymbolInfo(receiver).Symbol as ITypeSymbol;
            if (t == null || t.TypeKind == TypeKind.Error) return;
            info.missingMember = member;
            info.receiverTypes = TypeChain(t);
            return;
        }

        // Missing type / name.
        string name = null;
        var namespaces = new List<string>();
        if (node is QualifiedNameSyntax q0) node = q0.Right;
        if (node is SimpleNameSyntax sn)
        {
            name = sn.Identifier.Text;
            if (sn.Parent is QualifiedNameSyntax qn && qn.Right == sn) namespaces.Add(qn.Left.ToString().Replace("global::", ""));
            else if (sn.Parent is MemberAccessExpressionSyntax ma && ma.Name == sn) namespaces.Add(ma.Expression.ToString().Replace("global::", ""));
        }
        if (name == null) return;
        info.missingType = name;
        // using directives + enclosing namespaces
        foreach (var u in root.DescendantNodes(x => x is CompilationUnitSyntax || x is BaseNamespaceDeclarationSyntax).OfType<UsingDirectiveSyntax>())
            if (u.StaticKeyword.IsKind(SyntaxKind.None) && u.Alias == null) namespaces.Add(u.Name.ToString().Replace("global::", ""));
        foreach (var ns in node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>())
        {
            var parts = ns.Name.ToString().Split('.');
            for (int i = parts.Length; i > 0; i--) namespaces.Add(string.Join('.', parts.Take(i)));
        }
        namespaces.Add("");
        info.candidateNamespaces = namespaces.Distinct().ToList();
    }

    static List<string> TypeChain(ITypeSymbol t)
    {
        var list = new List<string>();
        for (var cur = t; cur != null; cur = cur.BaseType)
        {
            if (cur.SpecialType == SpecialType.System_Object) break;
            list.Add(FullName(cur));
        }
        if (t is ITypeParameterSymbol tp)
            foreach (var c in tp.ConstraintTypes) list.AddRange(TypeChain(c));
        foreach (var i in t.AllInterfaces) list.Add(FullName(i));
        return list.Distinct().ToList();
    }

    public static string FullName(ITypeSymbol t)
    {
        if (t is IArrayTypeSymbol) return "System.Array";
        var parts = new List<string>();
        ISymbol cur = t.OriginalDefinition;
        while (cur is INamedTypeSymbol nt) { parts.Insert(0, nt.Name); cur = nt.ContainingType ?? (ISymbol)nt.ContainingNamespace; if (cur is INamespaceSymbol) break; }
        var ns = t.ContainingNamespace;
        var nsName = ns == null || ns.IsGlobalNamespace ? "" : ns.ToDisplayString();
        return (nsName.Length > 0 ? nsName + "." : "") + string.Join('.', parts);
    }

    // Semantic pass: every symbol resolved into a UnityEngine*/UnityEditor* assembly is checked against the UnityCsReference
    // index of the project's Unity version.
    static List<ApiUse> Unity6Audit(CSharpCompilation comp, Unity6Index index, List<string> prefixes)
    {
        var uses = new ConcurrentBag<ApiUse>();
        Parallel.ForEach(comp.SyntaxTrees, tree =>
        {
            var model = comp.GetSemanticModel(tree);
            var seen = new HashSet<string>();
            foreach (var node in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
            {
                var sym = model.GetSymbolInfo(node).Symbol;
                if (sym == null) continue;
                if (sym is IMethodSymbol m && m.ReducedFrom != null) sym = m.ReducedFrom;
                if (sym is IMethodSymbol m2 && m2.MethodKind == MethodKind.Constructor) continue;
                var asm = sym.ContainingAssembly?.Name ?? "";
                if (!prefixes.Any(p => asm.StartsWith(p, StringComparison.Ordinal))) continue;
                // package assemblies built from real sources carry their own [Obsolete] attributes (CS0618 covers them)
                if (asm == "UnityEngine.UI" || asm == "UnityEditor.UI" || asm.StartsWith("UnityEngine.Purchasing", StringComparison.Ordinal) || asm.StartsWith("UnityEngine.Unity6Shims", StringComparison.Ordinal)) continue;

                string typeName, memberName; int arity = -1;
                if (sym is INamedTypeSymbol nts) { typeName = FullName(nts); memberName = null; }
                else if (sym.ContainingType != null)
                {
                    typeName = FullName(sym.ContainingType);
                    memberName = sym.Name;
                    if (sym is IPropertySymbol ps && ps.IsIndexer) memberName = "this[]";
                    if (sym is IMethodSymbol ms) arity = ms.Parameters.Length;
                }
                else continue;

                var pos = node.GetLocation().GetMappedLineSpan();
                string key = typeName + "|" + memberName + "|" + pos.StartLinePosition.Line;
                if (!seen.Add(key)) continue;

                var api = memberName == null ? typeName : typeName + "." + memberName;
                var res = index.Check(typeName, memberName, arity);
                if (res == null) continue;
                uses.Add(new ApiUse
                {
                    kind = res.Value.kind,
                    api = api,
                    file = pos.Path,
                    line = pos.StartLinePosition.Line + 1,
                    col = pos.StartLinePosition.Character + 1,
                    message = res.Value.message,
                    error = res.Value.error,
                });
            }
        });
        return uses.OrderBy(u => u.file).ThenBy(u => u.line).ThenBy(u => u.col).ToList();
    }
}

public sealed class Unity6Index
{
    // type full name -> member name -> rows
    readonly Dictionary<string, Dictionary<string, List<(int arity, bool obsolete, bool error, string msg)>>> m_Members = new(StringComparer.Ordinal);
    readonly Dictionary<string, (bool obsolete, bool error, string msg)> m_Types = new(StringComparer.Ordinal);

    string m_Version = "6000.x";

    public static Unity6Index Load(string path, string version)
    {
        var idx = new Unity6Index { m_Version = version };
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("#")) continue;
            var c = line.Split('\t');
            if (c.Length < 9) continue;
            // kind ns type member arity access obsolete error message file line
            string kind = c[0], ns = c[1], type = c[2], member = c[3];
            int.TryParse(c[4], out var arity);
            bool obs = c[6] == "1", err = c[7] == "1";
            string msg = c[8];
            var full = ns.Length > 0 ? ns + "." + type : type;
            if (kind == "T")
            {
                // a type whose only rows are modifier-less partial parts is not (known to be) public
                if (c[5] == "partial") continue;
                if (!idx.m_Types.TryGetValue(full, out var prev) || (!prev.obsolete && obs))
                    idx.m_Types[full] = (obs, err, msg);
                continue;
            }
            if (!idx.m_Members.TryGetValue(full, out var members)) idx.m_Members[full] = members = new(StringComparer.Ordinal);
            if (!members.TryGetValue(member, out var rows)) members[member] = rows = new();
            rows.Add((kind == "M" || kind == "C" ? arity : -1, obs, err, msg));
        }
        return idx;
    }

    public (string kind, string message, bool error)? Check(string type, string member, int arity)
    {
        if (!m_Types.TryGetValue(type, out var t))
            return ("missing", $"type not found in UnityCsReference {m_Version} (removed, renamed, internal, or not part of the published C# reference)", false);
        if (t.obsolete)
            return ("obsolete", t.msg, t.error);
        if (member == null) return null;
        if (!m_Members.TryGetValue(type, out var members) || !members.TryGetValue(member, out var rows))
        {
            // Members inherited from a base type are reported against the declaring type by Roslyn, so a miss here
            // means the declaring type exists in that version but this member does not (in the C# reference source).
            return ("missing", $"member not found on this type in UnityCsReference {m_Version} (removed, internal, or declared in unpublished/native code)", false);
        }
        IEnumerable<(int arity, bool obsolete, bool error, string msg)> cand = rows;
        if (arity >= 0)
        {
            var byArity = rows.Where(r => r.arity == arity || r.arity < 0).ToList();
            if (byArity.Count > 0) cand = byArity;
        }
        // Obsolete only if every candidate overload is obsolete.
        var list = cand.ToList();
        if (list.All(r => r.obsolete))
        {
            var e = list.Any(r => r.error);
            return ("obsolete", list.First(r => r.error == e).msg, e);
        }
        return null;
    }
}

// ------------------------------------------------------------------------------------------------
// body stripping (package sources that target a newer engine than the reference assemblies)
// ------------------------------------------------------------------------------------------------

// For a compile check only the public API surface of a package assembly matters. When package sources target a newer
// engine than the available engine reference assemblies (URP 17.3 = Unity 6000.3 vs. 6000.0.58 player assemblies),
// method bodies that use engine APIs missing from the refs are replaced with `{ throw null; }` (field / property
// initializers with `default`), iterating until no error is left inside a body. Declarations (types, member
// signatures, attributes incl. [Obsolete]) are never touched; errors in declarations are still reported.
public static class BodyStripper
{
    const string ThrowBody = "{ throw null; }";

    public static CSharpCompilation Strip(CSharpCompilation comp, List<string> log, int maxRounds = 10)
    {
        for (int round = 0; round < maxRounds; round++)
        {
            var errors = comp.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error && d.Location.IsInSource).ToList();
            if (errors.Count == 0) break;
            var edits = new Dictionary<SyntaxTree, List<(TextSpan span, string text, string what)>>();
            foreach (var d in errors)
            {
                var tree = d.Location.SourceTree;
                var token = tree.GetRoot().FindToken(d.Location.SourceSpan.Start);
                var edit = FindEdit(token.Parent, d.Location.SourceSpan);
                if (edit == null) continue;
                if (!edits.TryGetValue(tree, out var list)) edits[tree] = list = new();
                list.Add(edit.Value);
            }
            if (edits.Count == 0) break;
            foreach (var (tree, list) in edits)
            {
                // syntax spans nest properly: sorted by start (outer first), skip everything inside the last kept edit
                var kept = new List<(TextSpan span, string text, string what)>();
                foreach (var e in list.OrderBy(e => e.span.Start).ThenByDescending(e => e.span.Length))
                    if (kept.Count == 0 || kept[^1].span.End <= e.span.Start) kept.Add(e);
                var text = tree.GetText();
                var newTree = tree.WithChangedText(text.WithChanges(kept.Select(e => new TextChange(e.span, e.text))));
                comp = comp.ReplaceSyntaxTree(tree, newTree);
                foreach (var e in kept)
                    log.Add($"{tree.FilePath}({text.Lines.GetLinePosition(e.span.Start).Line + 1}): {e.what}");
            }
        }
        return comp;
    }

    static string Name(SyntaxNode n)
    {
        var type = n.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text ?? "?";
        string member = n switch
        {
            MethodDeclarationSyntax m => m.Identifier.Text + "()",
            ConstructorDeclarationSyntax => ".ctor",
            DestructorDeclarationSyntax => "~dtor",
            OperatorDeclarationSyntax o => "operator " + o.OperatorToken.Text,
            ConversionOperatorDeclarationSyntax c => "operator " + c.Type,
            PropertyDeclarationSyntax p => p.Identifier.Text,
            IndexerDeclarationSyntax => "this[]",
            EventDeclarationSyntax e => e.Identifier.Text,
            AccessorDeclarationSyntax a => Name(a.Parent.Parent).Split('.').Last() + "." + a.Keyword.Text,
            VariableDeclaratorSyntax v => v.Identifier.Text,
            _ => n.Kind().ToString(),
        };
        return type + "." + member;
    }

    static (TextSpan, string, string)? FindEdit(SyntaxNode node, TextSpan err)
    {
        for (var n = node; n != null; n = n.Parent)
        {
            switch (n)
            {
                case AccessorDeclarationSyntax acc:
                    if (acc.Body != null && acc.Body.Span.Contains(err)) return (acc.Body.Span, ThrowBody, Name(acc) + " body");
                    if (acc.ExpressionBody != null && acc.ExpressionBody.Span.Contains(err))
                        return (TextSpan.FromBounds(acc.ExpressionBody.SpanStart, acc.SemicolonToken.Span.End), ThrowBody, Name(acc) + " body");
                    return null;
                case BaseMethodDeclarationSyntax m:
                    if (m.Body != null && m.Body.Span.Contains(err)) return (m.Body.Span, ThrowBody, Name(m) + " body");
                    if (m.ExpressionBody != null && m.ExpressionBody.Span.Contains(err))
                        return (TextSpan.FromBounds(m.ExpressionBody.SpanStart, m.SemicolonToken.Span.End), ThrowBody, Name(m) + " body");
                    return null;
                case PropertyDeclarationSyntax p:
                    if (p.ExpressionBody != null && p.ExpressionBody.Span.Contains(err))
                        return (TextSpan.FromBounds(p.ExpressionBody.SpanStart, p.SemicolonToken.Span.End), "{ get " + ThrowBody + " }", Name(p) + " getter");
                    if (p.Initializer != null && p.Initializer.Value.Span.Contains(err)) return (p.Initializer.Value.Span, "default", Name(p) + " initializer");
                    return null;
                case IndexerDeclarationSyntax ix:
                    if (ix.ExpressionBody != null && ix.ExpressionBody.Span.Contains(err))
                        return (TextSpan.FromBounds(ix.ExpressionBody.SpanStart, ix.SemicolonToken.Span.End), "{ get " + ThrowBody + " }", Name(ix) + " getter");
                    return null;
                case VariableDeclaratorSyntax v when v.Parent?.Parent is BaseFieldDeclarationSyntax:
                    if (v.Initializer != null && v.Initializer.Value.Span.Contains(err)) return (v.Initializer.Value.Span, "default", Name(v) + " initializer");
                    return null;
                case MemberDeclarationSyntax:
                    return null;
            }
        }
        return null;
    }
}

// ------------------------------------------------------------------------------------------------
// index (UnityCsReference)
// ------------------------------------------------------------------------------------------------

public static class CsRefIndexer
{
    public static int Run(string root, string outPath)
    {
        root = Path.GetFullPath(root);
        var dirs = new[] { "Runtime", "Modules", "Editor", "artifacts/generated" }.Select(d => Path.Combine(root, d)).Where(Directory.Exists).ToList();
        var files = dirs.SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains("/Tests/") && !f.Contains("/Test/"))
            .OrderBy(f => f, StringComparer.Ordinal).ToList();

        // Superset of preprocessor symbols: UNITY_EDITOR + every ENABLE_* / PLATFORM_SUPPORTS_* symbol used anywhere.
        var symbols = new HashSet<string>(new[] { "UNITY_EDITOR", "UNITY_EDITOR_64", "UNITY_64", "UNITY_ASSERTIONS", "UNITY_2023_1_OR_NEWER", "UNITY_6000_0_OR_NEWER",
                                                   "UNITY_6000_1_OR_NEWER", "UNITY_6000_2_OR_NEWER", "UNITY_6000_3_OR_NEWER" });
        var rx = new System.Text.RegularExpressions.Regex(@"\b(ENABLE_[A-Z0-9_]+|PLATFORM_SUPPORTS_[A-Z0-9_]+)\b");
        foreach (var f in files)
            foreach (var line in File.ReadLines(f))
            {
                var t = line.TrimStart();
                if (t.StartsWith("#if") || t.StartsWith("#elif"))
                    foreach (System.Text.RegularExpressions.Match m in rx.Matches(t)) symbols.Add(m.Value);
            }
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.None, SourceCodeKind.Regular, symbols);

        var rows = new ConcurrentBag<string>();
        Parallel.ForEach(files, f =>
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(f), parseOptions, f);
            var rel = Path.GetRelativePath(root, f);
            var walker = new IndexWalker(rel, rows);
            walker.Visit(tree.GetRoot());
        });
        var sorted = rows.Distinct().OrderBy(r => r, StringComparer.Ordinal).ToList();
        using var w = new StreamWriter(outPath, false, new UTF8Encoding(false));
        w.WriteLine("#kind\tnamespace\ttype\tmember\tarity\taccess\tobsolete\tobsoleteError\tobsoleteMessage\tfile\tline");
        foreach (var r in sorted) w.WriteLine(r);
        Console.Error.WriteLine($"[ucctool] indexed {files.Count} files -> {sorted.Count} rows ({outPath})");
        return 0;
    }

    sealed class IndexWalker : CSharpSyntaxWalker
    {
        readonly string m_File; readonly ConcurrentBag<string> m_Rows;
        public IndexWalker(string file, ConcurrentBag<string> rows) { m_File = file; m_Rows = rows; }

        static string Ns(SyntaxNode n)
        {
            var parts = n.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Select(x => x.Name.ToString()).Reverse();
            return string.Join('.', parts);
        }
        static string TypePath(BaseTypeDeclarationSyntax t)
        {
            var names = t.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().Select(x => x.Identifier.Text).Reverse();
            return string.Join('.', names);
        }
        static bool IsVisibleType(BaseTypeDeclarationSyntax t)
        {
            foreach (var x in t.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>())
            {
                var mods = x.Modifiers;
                bool isPublic = mods.Any(SyntaxKind.PublicKeyword) || (mods.Any(SyntaxKind.ProtectedKeyword) && !mods.Any(SyntaxKind.PrivateKeyword));
                // partial parts without modifiers: accessibility declared on another part - accept, filtered by merge later
                if (!isPublic && !(mods.Any(SyntaxKind.PartialKeyword) && !mods.Any(SyntaxKind.InternalKeyword) && !mods.Any(SyntaxKind.PrivateKeyword) && !mods.Any(SyntaxKind.ProtectedKeyword)))
                    return false;
            }
            return true;
        }
        static string Access(SyntaxTokenList mods, bool inInterfaceOrEnum)
        {
            if (inInterfaceOrEnum) return "public";
            if (mods.Any(SyntaxKind.PublicKeyword)) return "public";
            if (mods.Any(SyntaxKind.ProtectedKeyword) && !mods.Any(SyntaxKind.PrivateKeyword)) return "protected";
            return null;
        }

        static (bool obs, bool err, string msg) Obsolete(SyntaxList<AttributeListSyntax> lists)
        {
            foreach (var al in lists)
                foreach (var a in al.Attributes)
                {
                    var n = a.Name.ToString();
                    if (n != "Obsolete" && n != "ObsoleteAttribute" && n != "System.Obsolete" && n != "System.ObsoleteAttribute") continue;
                    string msg = ""; bool err = false;
                    var args = a.ArgumentList?.Arguments;
                    if (args != null)
                    {
                        int pos = 0;
                        foreach (var arg in args)
                        {
                            var argName = arg.NameColon?.Name.Identifier.Text;
                            if (argName == "error" || (argName == null && pos == 1)) err = arg.Expression.IsKind(SyntaxKind.TrueLiteralExpression);
                            else if (argName == "message" || (argName == null && pos == 0)) msg = StringValue(arg.Expression);
                            pos++;
                        }
                    }
                    return (true, err, msg);
                }
            return (false, false, "");
        }

        static string StringValue(ExpressionSyntax e)
        {
            switch (e)
            {
                case LiteralExpressionSyntax l when l.IsKind(SyntaxKind.StringLiteralExpression): return l.Token.ValueText;
                case BinaryExpressionSyntax b when b.IsKind(SyntaxKind.AddExpression): return StringValue(b.Left) + StringValue(b.Right);
                case InterpolatedStringExpressionSyntax i: return string.Concat(i.Contents.Select(c => c is InterpolatedStringTextSyntax t ? t.TextToken.ValueText : "{" + ((InterpolationSyntax)c).Expression + "}"));
                default: return e.ToString();
            }
        }

        void Row(string kind, string ns, string type, string member, int arity, string access, (bool obs, bool err, string msg) o, SyntaxNode n)
        {
            var line = n.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
            m_Rows.Add($"{kind}\t{ns}\t{type}\t{Clean(member)}\t{arity}\t{access}\t{(o.obs ? 1 : 0)}\t{(o.err ? 1 : 0)}\t{Clean(o.msg)}\t{m_File}\t{line}");
        }

        void VisitType(BaseTypeDeclarationSyntax t)
        {
            if (!IsVisibleType(t)) return;
            var ns = Ns(t); var tp = TypePath(t);
            var access = t.Modifiers.Any(SyntaxKind.PublicKeyword) ? "public" : t.Modifiers.Any(SyntaxKind.ProtectedKeyword) ? "protected" : "partial";
            var tobs = Obsolete(t.AttributeLists);
            Row("T", ns, tp, "", -1, access, tobs, t);
            bool ifaceOrEnum = t is InterfaceDeclarationSyntax || t is EnumDeclarationSyntax;
            if (t is EnumDeclarationSyntax en)
            {
                foreach (var m in en.Members) Row("F", ns, tp, m.Identifier.Text, -1, "public", Obsolete(m.AttributeLists), m);
                return;
            }
            if (t is TypeDeclarationSyntax td)
            {
                if (td.ParameterList != null) // record / primary ctor
                    Row("C", ns, tp, ".ctor", td.ParameterList.Parameters.Count, "public", (false, false, ""), td);
                foreach (var m in td.Members)
                {
                    switch (m)
                    {
                        case MethodDeclarationSyntax md:
                            if (md.ExplicitInterfaceSpecifier != null) break;
                            { var a = Access(md.Modifiers, ifaceOrEnum); if (a != null) Row("M", ns, tp, md.Identifier.Text, md.ParameterList.Parameters.Count, a, Obsolete(md.AttributeLists), md); }
                            break;
                        case ConstructorDeclarationSyntax cd:
                            { var a = Access(cd.Modifiers, false); if (a != null && !cd.Modifiers.Any(SyntaxKind.StaticKeyword)) Row("C", ns, tp, ".ctor", cd.ParameterList.Parameters.Count, a, Obsolete(cd.AttributeLists), cd); }
                            break;
                        case PropertyDeclarationSyntax pd:
                            if (pd.ExplicitInterfaceSpecifier != null) break;
                            { var a = Access(pd.Modifiers, ifaceOrEnum); if (a != null) Row("P", ns, tp, pd.Identifier.Text, -1, a, Obsolete(pd.AttributeLists), pd); }
                            break;
                        case IndexerDeclarationSyntax ix:
                            if (ix.ExplicitInterfaceSpecifier != null) break;
                            { var a = Access(ix.Modifiers, ifaceOrEnum); if (a != null) Row("P", ns, tp, "this[]", -1, a, Obsolete(ix.AttributeLists), ix); }
                            break;
                        case FieldDeclarationSyntax fd:
                            { var a = Access(fd.Modifiers, false); if (a != null) foreach (var v in fd.Declaration.Variables) Row("F", ns, tp, v.Identifier.Text, -1, a, Obsolete(fd.AttributeLists), v); }
                            break;
                        case EventFieldDeclarationSyntax ef:
                            { var a = Access(ef.Modifiers, ifaceOrEnum); if (a != null) foreach (var v in ef.Declaration.Variables) Row("E", ns, tp, v.Identifier.Text, -1, a, Obsolete(ef.AttributeLists), v); }
                            break;
                        case EventDeclarationSyntax ed:
                            if (ed.ExplicitInterfaceSpecifier != null) break;
                            { var a = Access(ed.Modifiers, ifaceOrEnum); if (a != null) Row("E", ns, tp, ed.Identifier.Text, -1, a, Obsolete(ed.AttributeLists), ed); }
                            break;
                        case OperatorDeclarationSyntax od:
                            Row("M", ns, tp, "op_" + od.OperatorToken.Text, od.ParameterList.Parameters.Count, "public", Obsolete(od.AttributeLists), od);
                            break;
                        case ConversionOperatorDeclarationSyntax co:
                            Row("M", ns, tp, co.ImplicitOrExplicitKeyword.Text == "implicit" ? "op_Implicit" : "op_Explicit", 1, "public", Obsolete(co.AttributeLists), co);
                            break;
                        case DelegateDeclarationSyntax dd:
                            { var a = Access(dd.Modifiers, ifaceOrEnum); if (a != null) Row("T", ns, tp + "." + dd.Identifier.Text, "", -1, a, Obsolete(dd.AttributeLists), dd); }
                            break;
                    }
                }
            }
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node) { VisitType(node); base.VisitClassDeclaration(node); }
        public override void VisitStructDeclaration(StructDeclarationSyntax node) { VisitType(node); base.VisitStructDeclaration(node); }
        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) { VisitType(node); base.VisitInterfaceDeclaration(node); }
        public override void VisitEnumDeclaration(EnumDeclarationSyntax node) { VisitType(node); }
        public override void VisitRecordDeclaration(RecordDeclarationSyntax node) { VisitType(node); base.VisitRecordDeclaration(node); }
        public override void VisitDelegateDeclaration(DelegateDeclarationSyntax node)
        {
            if (node.Parent is BaseNamespaceDeclarationSyntax || node.Parent is CompilationUnitSyntax)
            {
                if (node.Modifiers.Any(SyntaxKind.PublicKeyword))
                    Row("T", Ns(node), node.Identifier.Text, "", -1, "public", Obsolete(node.AttributeLists), node);
            }
        }
        // don't descend into method bodies
        public override void VisitBlock(BlockSyntax node) { }
        public override void VisitArrowExpressionClause(ArrowExpressionClauseSyntax node) { }
    }
}

// ------------------------------------------------------------------------------------------------
// dump-refs (public API of reference assemblies)
// ------------------------------------------------------------------------------------------------

public static class RefDumper
{
    public static int Run(string outPath, string[] dlls)
    {
        using var w = new StreamWriter(outPath, false, new UTF8Encoding(false));
        w.WriteLine("#assembly\tkind\tnamespace\ttype\tmember");
        foreach (var path in dlls)
        {
            using var fs = File.OpenRead(path);
            using var pe = new PEReader(fs);
            if (!pe.HasMetadata) continue;
            var md = pe.GetMetadataReader();
            if (!md.IsAssembly) continue;
            var asm = md.GetString(md.GetAssemblyDefinition().Name);
            foreach (var th in md.TypeDefinitions)
            {
                var td = md.GetTypeDefinition(th);
                if (!Visible(md, td)) continue;
                var (ns, tn) = Names(md, td);
                w.WriteLine($"{asm}\tT\t{ns}\t{tn}\t");
                foreach (var mh in td.GetMethods())
                {
                    var m = md.GetMethodDefinition(mh);
                    var a = m.Attributes & MethodAttributes.MemberAccessMask;
                    if (a != MethodAttributes.Public && a != MethodAttributes.Family && a != MethodAttributes.FamORAssem) continue;
                    var name = md.GetString(m.Name);
                    if ((m.Attributes & MethodAttributes.SpecialName) != 0 && (name.StartsWith("get_") || name.StartsWith("set_") || name.StartsWith("add_") || name.StartsWith("remove_"))) continue;
                    w.WriteLine($"{asm}\tM\t{ns}\t{tn}\t{name}");
                }
                foreach (var fh in td.GetFields())
                {
                    var f = md.GetFieldDefinition(fh);
                    var a = f.Attributes & FieldAttributes.FieldAccessMask;
                    if (a != FieldAttributes.Public && a != FieldAttributes.Family && a != FieldAttributes.FamORAssem) continue;
                    var name = md.GetString(f.Name);
                    if (name == "value__") continue;
                    w.WriteLine($"{asm}\tF\t{ns}\t{tn}\t{name}");
                }
                foreach (var ph in td.GetProperties()) w.WriteLine($"{asm}\tP\t{ns}\t{tn}\t{md.GetString(md.GetPropertyDefinition(ph).Name)}");
                foreach (var eh in td.GetEvents()) w.WriteLine($"{asm}\tE\t{ns}\t{tn}\t{md.GetString(md.GetEventDefinition(eh).Name)}");
            }
            foreach (var eth in md.ExportedTypes)
            {
                var et = md.GetExportedType(eth);
                if (!et.IsForwarder) continue;
                w.WriteLine($"{asm}\tFWD\t{md.GetString(et.Namespace)}\t{md.GetString(et.Name)}\t");
            }
        }
        return 0;
    }

    static bool Visible(MetadataReader md, TypeDefinition td)
    {
        var vis = td.Attributes & TypeAttributes.VisibilityMask;
        if (vis == TypeAttributes.Public) return true;
        if (vis == TypeAttributes.NestedPublic || vis == TypeAttributes.NestedFamily || vis == TypeAttributes.NestedFamORAssem)
            return Visible(md, md.GetTypeDefinition(td.GetDeclaringType()));
        return false;
    }

    static (string ns, string name) Names(MetadataReader md, TypeDefinition td)
    {
        var name = StripArity(md.GetString(td.Name));
        var ns = md.GetString(td.Namespace);
        var decl = td.GetDeclaringType();
        while (!decl.IsNil)
        {
            var d = md.GetTypeDefinition(decl);
            name = StripArity(md.GetString(d.Name)) + "." + name;
            ns = md.GetString(d.Namespace);
            decl = d.GetDeclaringType();
        }
        return (ns, name);
    }

    static string StripArity(string n) { var i = n.IndexOf('`'); return i < 0 ? n : n.Substring(0, i); }
}
