// The game's stack traces name the method and an IL offset ("CAbilityAttack.StartAttackingActors () [0x00053]"), never
// a line: this prints the IL around that offset with every call and field resolved to a name, so the decompiled
// source (which may not match the shipped build byte for byte) can be read against the exact instruction that threw.
//
//     dotnet run --project tools/ilat -c Release -- <dll> <Type>::<Method> <hex offset> [context instructions]
//     dotnet run --project tools/ilat -c Release -- ScenarioRuleLibrary CAbilityAttack::StartAttackingActors 0x53
//
// A bare dll name is looked up in the game's Managed folder. The offset marks the instruction that faulted (a
// NullReference at a callvirt/ldfld/stfld is the receiver of THAT instruction). Overloads are all printed.
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

if (args.Length < 3) { Console.Error.WriteLine("usage: ilat <dll> <Type>::<Method> <hex offset> [context]"); return 2; }
string dll = args[0];
if (!File.Exists(dll))
{
    string game = Environment.GetEnvironmentVariable("FH_GAME") ?? @"C:\Program Files (x86)\Steam\steamapps\common\Frosthaven";
    string candidate = Path.Combine(game, "Frosthaven_Data", "Managed", dll.EndsWith(".dll") ? dll : dll + ".dll");
    if (!File.Exists(candidate)) { Console.Error.WriteLine("no such dll: " + dll); return 2; }
    dll = candidate;
}
string[] target = args[1].Split("::");
if (target.Length != 2) { Console.Error.WriteLine("method as Type::Method"); return 2; }
int offset = Convert.ToInt32(args[2].StartsWith("0x") ? args[2].Substring(2) : args[2], 16);
int context = args.Length > 3 ? int.Parse(args[3]) : 12;

using var pe = new PEReader(File.OpenRead(dll));
MetadataReader md = pe.GetMetadataReader();

// one-byte and two-byte opcodes, from System.Reflection.Emit's own table
var one = new Dictionary<int, OpCode>();
var two = new Dictionary<int, OpCode>();
foreach (var f in typeof(OpCodes).GetFields())
{
    if (f.GetValue(null) is not OpCode op) continue;
    if (op.Size == 1) one[op.Value & 0xff] = op; else two[op.Value & 0xff] = op;
}

string TypeName(EntityHandle h)
{
    switch (h.Kind)
    {
        case HandleKind.TypeDefinition: { var t = md.GetTypeDefinition((TypeDefinitionHandle)h); return md.GetString(t.Name); }
        case HandleKind.TypeReference: { var t = md.GetTypeReference((TypeReferenceHandle)h); return md.GetString(t.Name); }
        case HandleKind.TypeSpecification: return "<spec>";
        default: return h.Kind.ToString();
    }
}

string Member(int token)
{
    if ((token >> 24) == 0x70) return "\"" + md.GetUserString(MetadataTokens.UserStringHandle(token)) + "\"";
    EntityHandle h = MetadataTokens.EntityHandle(token);
    switch (h.Kind)
    {
        case HandleKind.MethodDefinition:
        {
            var m = md.GetMethodDefinition((MethodDefinitionHandle)h);
            return TypeName(m.GetDeclaringType()) + "." + md.GetString(m.Name);
        }
        case HandleKind.FieldDefinition:
        {
            var f = md.GetFieldDefinition((FieldDefinitionHandle)h);
            return TypeName(f.GetDeclaringType()) + "." + md.GetString(f.Name);
        }
        case HandleKind.MemberReference:
        {
            var r = md.GetMemberReference((MemberReferenceHandle)h);
            return TypeName(r.Parent) + "." + md.GetString(r.Name);
        }
        case HandleKind.MethodSpecification:
        {
            var s = md.GetMethodSpecification((MethodSpecificationHandle)h);
            return Member(MetadataTokens.GetToken(s.Method)) + "<...>";
        }
        case HandleKind.TypeDefinition: case HandleKind.TypeReference: case HandleKind.TypeSpecification: return TypeName(h);
        default: return h.Kind + ":" + token.ToString("x8");
    }
}

int found = 0;
foreach (TypeDefinitionHandle th in md.TypeDefinitions)
{
    TypeDefinition td = md.GetTypeDefinition(th);
    if (md.GetString(td.Name) != target[0]) continue;
    foreach (MethodDefinitionHandle mh in td.GetMethods())
    {
        MethodDefinition mdef = md.GetMethodDefinition(mh);
        if (md.GetString(mdef.Name) != target[1] || mdef.RelativeVirtualAddress == 0) continue;
        found++;
        MethodBodyBlock body = pe.GetMethodBody(mdef.RelativeVirtualAddress);
        byte[] il = body.GetILBytes();
        Console.WriteLine($"{target[0]}::{target[1]} (token {MetadataTokens.GetToken(mh):x8}, {il.Length} IL bytes, max stack {body.MaxStack})");
        // decode every instruction: (offset, text)
        var lines = new List<(int off, string text)>();
        int p = 0;
        while (p < il.Length)
        {
            int start = p;
            OpCode op;
            if (il[p] == 0xfe) { op = two[il[p + 1]]; p += 2; } else { op = one[il[p]]; p += 1; }
            string operand = "";
            switch (op.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineBrTarget: { int d = (sbyte)il[p]; p += 1; operand = "IL_" + (p + d).ToString("x4"); break; }
                case OperandType.InlineBrTarget: { int d = BitConverter.ToInt32(il, p); p += 4; operand = "IL_" + (p + d).ToString("x4"); break; }
                case OperandType.ShortInlineI: case OperandType.ShortInlineVar: operand = il[p].ToString(); p += 1; break;
                case OperandType.InlineVar: operand = BitConverter.ToUInt16(il, p).ToString(); p += 2; break;
                case OperandType.InlineI: operand = BitConverter.ToInt32(il, p).ToString(); p += 4; break;
                case OperandType.InlineI8: operand = BitConverter.ToInt64(il, p).ToString(); p += 8; break;
                case OperandType.ShortInlineR: operand = BitConverter.ToSingle(il, p).ToString(); p += 4; break;
                case OperandType.InlineR: operand = BitConverter.ToDouble(il, p).ToString(); p += 8; break;
                case OperandType.InlineSwitch:
                {
                    int n = BitConverter.ToInt32(il, p); p += 4 + 4 * n; operand = $"({n} targets)"; break;
                }
                case OperandType.InlineString: case OperandType.InlineMethod: case OperandType.InlineField:
                case OperandType.InlineType: case OperandType.InlineTok: case OperandType.InlineSig:
                {
                    int tok = BitConverter.ToInt32(il, p); p += 4;
                    operand = op.OperandType == OperandType.InlineSig ? "sig" : Member(tok);
                    break;
                }
                default: p += 4; operand = "?"; break;
            }
            lines.Add((start, $"{op.Name,-12} {operand}"));
        }
        int at = lines.FindIndex(l => l.off >= offset);
        if (at < 0) at = lines.Count - 1;
        if (lines[at].off != offset) Console.WriteLine($"  (no instruction starts at {offset:x4}; nearest after is {lines[at].off:x4})");
        for (int i = Math.Max(0, at - context); i < Math.Min(lines.Count, at + context / 2 + 1); i++)
            Console.WriteLine($"  {(lines[i].off == offset ? ">>" : "  ")} IL_{lines[i].off:x4}: {lines[i].text}");
    }
}
if (found == 0) { Console.Error.WriteLine($"no method {args[1]} with a body in {dll}"); return 1; }
return 0;
