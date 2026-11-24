// SPDX-License-Identifier: MIT
// Prints the public API of an assembly: every public type and every public or protected member, one sorted line each.
// A line changes when anything a dependent mod could bind to changes (name, kind, modifiers, types, parameter names and defaults).
// Usage: PublicApi <assembly.dll>
using Mono.Cecil;

static class Program
{
    static int Main(string[] args)
    {
        if (args.Length != 1) { Console.Error.WriteLine("usage: PublicApi <assembly.dll>"); return 2; }
        var asm = AssemblyDefinition.ReadAssembly(args[0]);
        var lines = new List<string>();
        foreach (var t in asm.MainModule.GetTypes())
        {
            if (!Visible(t)) continue;
            string tn = Name(t);
            lines.Add(tn + ": type " + TypeHeader(t));
            bool open = !t.IsSealed;   // protected members of sealed types cannot be reached
            foreach (var f in t.Fields)
            {
                if (f.IsSpecialName && t.IsEnum) continue;
                if (!(f.IsPublic || (open && (f.IsFamily || f.IsFamilyOrAssembly)))) continue;
                string mods = Access(f.IsPublic) + (f.IsLiteral ? " const" : f.IsStatic ? " static" : "") + (f.IsInitOnly ? " readonly" : "");
                string val = f.HasConstant ? " = " + Const(f.Constant) : "";
                lines.Add($"{tn}: field {mods} {Name(f.FieldType)} {f.Name}{val}");
            }
            foreach (var p in t.Properties)
            {
                var g = Reach(p.GetMethod, open) ? p.GetMethod : null; var s = Reach(p.SetMethod, open) ? p.SetMethod : null;
                if (g == null && s == null) continue;
                var any = g ?? s;
                string acc = (g != null ? Access(g.IsPublic) + " get; " : "") + (s != null ? Access(s.IsPublic) + " set; " : "");
                string ps = p.HasParameters ? "[" + string.Join(", ", p.Parameters.Select(Param)) + "]" : "";
                lines.Add($"{tn}: property{Mods(any)} {Name(p.PropertyType)} {p.Name}{ps} {{ {acc}}}");
            }
            foreach (var e in t.Events)
            {
                if (!Reach(e.AddMethod, open)) continue;
                lines.Add($"{tn}: event{Mods(e.AddMethod)} {Name(e.EventType)} {e.Name}");
            }
            foreach (var m in t.Methods)
            {
                if (m.IsGetter || m.IsSetter || m.IsAddOn || m.IsRemoveOn) continue;
                if (!Reach(m, open)) continue;
                string gen = m.HasGenericParameters ? "<" + string.Join(", ", m.GenericParameters.Select(GenParam)) + ">" : "";
                string name = m.IsConstructor ? (m.IsStatic ? ".cctor" : ".ctor") : m.Name;
                string ret = m.IsConstructor ? "" : " " + Name(m.ReturnType);
                lines.Add($"{tn}: method {Access(m.IsPublic)}{Mods(m)}{ret} {name}{gen}({string.Join(", ", m.Parameters.Select(Param))})");
            }
        }
        foreach (var a in asm.CustomAttributes)
            if (a.AttributeType.Name == "AssemblyVersionAttribute") lines.Add("assembly version " + a.ConstructorArguments[0].Value);
        lines.Add("assembly name " + asm.Name.Name + " " + asm.Name.Version);
        foreach (var l in lines.Distinct().OrderBy(x => x, StringComparer.Ordinal)) Console.WriteLine(l);
        return 0;
    }

    static bool Visible(TypeDefinition t)
    {
        for (var c = t; c != null; c = c.DeclaringType)
        {
            if (c.DeclaringType == null) { if (!c.IsPublic) return false; }
            else if (!(c.IsNestedPublic || ((c.IsNestedFamily || c.IsNestedFamilyOrAssembly) && !c.DeclaringType.IsSealed))) return false;
        }
        return !t.Name.StartsWith("<");
    }

    static bool Reach(MethodDefinition m, bool open) => m != null && (m.IsPublic || (open && (m.IsFamily || m.IsFamilyOrAssembly)));
    static string Access(bool isPublic) => isPublic ? "public" : "protected";

    static string Mods(MethodDefinition m)
    {
        var s = "";
        if (m.IsStatic) s += " static";
        if (m.IsAbstract) s += " abstract";
        else if (m.IsVirtual && m.IsFinal && !m.IsNewSlot) s += " sealed override";
        else if (m.IsVirtual && !m.IsNewSlot) s += " override";
        else if (m.IsVirtual && !m.IsFinal) s += " virtual";
        return s;
    }

    static string TypeHeader(TypeDefinition t)
    {
        string kind = t.IsEnum ? "enum" : t.IsInterface ? "interface" : t.IsValueType ? "struct" : (t.IsAbstract && t.IsSealed) ? "static class"
            : t.IsAbstract ? "abstract class" : t.IsSealed ? "sealed class" : "class";
        var bases = new List<string>();
        if (t.BaseType != null && !t.IsEnum && !t.IsValueType && t.BaseType.FullName != "System.Object") bases.Add(Name(t.BaseType));
        bases.AddRange(t.Interfaces.Select(i => Name(i.InterfaceType)));
        string gen = t.HasGenericParameters ? "<" + string.Join(", ", t.GenericParameters.Select(GenParam)) + ">" : "";
        return $"{kind} {Name(t)}{gen}" + (bases.Count > 0 ? " : " + string.Join(", ", bases) : "");
    }

    static string GenParam(GenericParameter g)
    {
        var c = new List<string>();
        if (g.HasNotNullableValueTypeConstraint) c.Add("struct");
        if (g.HasReferenceTypeConstraint) c.Add("class");
        c.AddRange(g.Constraints.Select(x => Name(x.ConstraintType)).Where(n => n != "System.ValueType"));
        if (g.HasDefaultConstructorConstraint && !g.HasNotNullableValueTypeConstraint) c.Add("new()");
        return g.Name + (c.Count > 0 ? " : " + string.Join(" + ", c) : "");
    }

    static string Param(ParameterDefinition p)
    {
        string mod = p.IsOut ? "out " : p.ParameterType.IsByReference ? "ref " : "";
        string def = p.HasConstant ? " = " + Const(p.Constant) : p.IsOptional ? " = ?" : "";
        return mod + Name(p.ParameterType) + " " + p.Name + def;
    }

    static string Const(object v) => v == null ? "null" : v is string s ? "\"" + s + "\"" : v is bool b ? (b ? "true" : "false")
        : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);

    static string Name(TypeReference t)
    {
        if (t is ByReferenceType br) return Name(br.ElementType);
        if (t is ArrayType at) return Name(at.ElementType) + "[]";
        if (t is GenericInstanceType gi)
            return Name(gi.ElementType).Split('`')[0] + "<" + string.Join(", ", gi.GenericArguments.Select(Name)) + ">";
        if (t is GenericParameter gp) return gp.Name;
        string n = t.FullName.Replace('/', '.');
        int tick = n.IndexOf('`');
        return tick >= 0 ? n.Substring(0, tick) : n;
    }
}
