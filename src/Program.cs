

static class Program
{
    static void Main()
    {
        var decls = new DeclParser(File.ReadAllText("code.txt")).Parse();
        new CEmitter(decls).Emit();
    }
}