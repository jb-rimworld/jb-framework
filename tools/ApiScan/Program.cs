// SPDX-License-Identifier: MIT
// Copied from Hearth & Harbor tools/ApiScan: lists every Harmony target of a build and checks that it exists in a RimWorld Assembly-CSharp.
// Usage: ApiScan <Hearth.dll> <Assembly-CSharp.dll> [more patch dlls...]   (exit 1 if any target is missing)
using Mono.Cecil;

static class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: ApiScan <patch.dll> <Assembly-CSharp.dll> [more patch dlls]"); return 2; }
        var resolver = new DefaultAssemblyResolver();
        foreach (var d in (Environment.GetEnvironmentVariable("JB_SEARCH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries)) resolver.AddSearchDirectory(d);
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1])));
        var rp = new ReaderParameters { AssemblyResolver = resolver };
        var game = AssemblyDefinition.ReadAssembly(args[1], rp);
        var types = game.MainModule.GetTypes().ToDictionary(t => t.FullName.Replace('/', '+'), t => t, StringComparer.Ordinal);
        var byShort = game.MainModule.GetTypes().GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.ToList());
        int ok = 0, missing = 0, dynamicN = 0, external = 0;
        foreach (var dll in new[] { args[0] }.Concat(args.Skip(2)))
        {
            var asm = AssemblyDefinition.ReadAssembly(dll, rp);
            foreach (var pt in asm.MainModule.GetTypes())
            {
                var classAttrs = pt.CustomAttributes.Where(a => a.AttributeType.Name == "HarmonyPatch").ToList();
                var methodAttrs = pt.Methods.SelectMany(m => m.CustomAttributes.Where(a => a.AttributeType.Name == "HarmonyPatch").Select(a => (m, a))).ToList();
                if (classAttrs.Count == 0 && methodAttrs.Count == 0) continue;
                bool hasDynamic = pt.Methods.Any(m => m.Name is "TargetMethod" or "TargetMethods");
                // combine class-level and method-level attributes the way Harmony does (member-wise union)
                var sets = methodAttrs.Count == 0 ? new List<List<CustomAttribute>> { classAttrs } : methodAttrs.Select(x => classAttrs.Concat(new[] { x.a }).ToList()).ToList();
                foreach (var set in sets)
                {
                    TypeReference tt = null; string name = null; int mt = 0; TypeReference[] ps = null; bool any = false;
                    foreach (var a in set)
                        foreach (var (arg, i) in a.ConstructorArguments.Select((x, i) => (x, i)))
                        {
                            any = true;
                            if (arg.Value is TypeReference tr && arg.Type.Name == "Type") tt = tr;
                            else if (arg.Value is string s) name = s;
                            else if (arg.Type.Name == "MethodType") mt = Convert.ToInt32(arg.Value);
                            else if (arg.Value is CustomAttributeArgument[] arr && arg.Type.IsArray && arg.Type.GetElementType().Name == "Type")
                                ps = arr.Select(e => (TypeReference)e.Value).ToArray();
                        }
                    string label = pt.FullName;
                    if (tt == null && name == null && hasDynamic) { Console.WriteLine($"DYNAMIC {label}  (TargetMethod/TargetMethods, resolved at runtime)"); dynamicN++; continue; }
                    if (tt == null) { Console.WriteLine($"DYNAMIC {label}  (no target type in attribute)"); dynamicN++; continue; }
                    string tn = tt.FullName.Replace('/', '+');
                    if (!types.TryGetValue(tn, out var td))
                    {
                        // type from another assembly (Unity, mscorlib, Harmony...) is out of scope; a game type that vanished is a miss
                        if (tt.Scope?.Name != null && tt.Scope.Name.StartsWith("Assembly-CSharp") || tn.StartsWith("RimWorld") || tn.StartsWith("Verse"))
                        { Console.WriteLine($"MISSING {label} -> type {tn} not found"); missing++; }
                        else { Console.WriteLine($"EXTERNAL {label} -> {tn}"); external++; }
                        continue;
                    }
                    // MethodType 3/4 (constructor / static constructor) need no name; checked like methods below (parameters included)
                    if (name == null && mt != 3 && mt != 4) { Console.WriteLine($"DYNAMIC {label} -> {tn} (no method name)"); dynamicN++; continue; }
                    // MethodType: 0 Normal, 1 Getter, 2 Setter, 3 Constructor, 4 StaticConstructor, 5 Enumerator
                    string find = mt == 1 ? "get_" + name : mt == 2 ? "set_" + name : mt == 3 ? ".ctor" : mt == 4 ? ".cctor" : name;
                    var cands = new List<MethodDefinition>();
                    for (var cur = td; cur != null; cur = cur.BaseType == null ? null : (types.TryGetValue(cur.BaseType.FullName.Replace('/', '+'), out var b) ? b : null))
                    { cands.AddRange(cur.Methods.Where(m => m.Name == find)); if (cands.Count > 0) break; }
                    if (cands.Count == 0) { Console.WriteLine($"MISSING {label} -> {tn}.{find} (method not found)"); missing++; continue; }
                    if (ps != null)
                    {
                        bool match = cands.Any(m => m.Parameters.Count == ps.Length &&
                            m.Parameters.Select((p, i) => (p, i)).All(x => Same(x.p.ParameterType, ps[x.i])));
                        if (!match)
                        {
                            bool countOnly = cands.Any(m => m.Parameters.Count == ps.Length);
                            Console.WriteLine($"MISSING {label} -> {tn}.{find}({string.Join(", ", ps.Select(p => p.Name))}) " + (countOnly ? "(count matches, parameter types differ)" : "(no overload with " + ps.Length + " parameters; has " + string.Join("|", cands.Select(c => c.Parameters.Count)) + ")"));
                            missing++; continue;
                        }
                    }
                    ok++;
                }
            }
        }
        Console.WriteLine($"-- ok {ok}, missing {missing}, dynamic {dynamicN}, external {external}");
        return missing > 0 ? 1 : 0;
    }

    static bool Same(TypeReference a, TypeReference b)
    {
        string n(TypeReference t) => t.FullName.Replace("&", "").Replace('/', '+');
        var x = n(a); var y = n(b);
        if (x == y) return true;
        return a.Name.TrimEnd('&') == b.Name.TrimEnd('&');   // generic instances / nullable wrappers: compare short names
    }
}
