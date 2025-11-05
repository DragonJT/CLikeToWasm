using System.Text;

static class CPrinter
{
    public static string PrintMany(IEnumerable<ICDecl> decls)
    {
        var sb = new StringBuilder();
        bool first = true;
        foreach (var d in decls)
        {
            if (!first) sb.AppendLine();
            if (d is CFunction cFunction) sb.AppendLine(PrintFunction(cFunction));
            else if (d is CConst cConst) sb.AppendLine(PrintConst(cConst));
            first = false;
        }
        return sb.ToString();
    }

    static string PrintConst(CConst c)
    {
        return $"const {c.Type} {c.Name} ={c.Expression.ToRawCode()};";
    }

    static string PrintFunction(CFunction f)
    {
        string storage = f.FunctionType switch
        {
            CFunctionType.Import => "import",
            CFunctionType.Export => "export",
            _ => ""
        };

        string paramList = string.Join(", ", f.Parameters.Select(p => $"{p.Type} {p.Name}"));
        var sig = $"{storage} {f.ReturnType} {f.Name}({paramList})" + f.Code.ToRawCode();
        return sig;
    }
}