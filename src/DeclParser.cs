using System.Text;

enum CFunctionType{ Regular, Import, Export }

interface ICDecl { }

record CParameter(string Type, string Name);

record CFunction(CFunctionType FunctionType, string ReturnType, string Name, CParameter[] Parameters, Parser Code) : ICDecl;

record CConst(string Type, string Name, Parser Expression): ICDecl;

class Parser
{
    int index = 0;
    readonly Token[] tokens;

    public int Index => index;
    
    public Parser(string source, string[] keywords)
    {
        var lexer = new Lexer(source, keywords);
        tokens = [.. lexer.Tokenize()];
    }

    Parser(Token[] tokens)
    {
        this.tokens = tokens;
    }

    public Token Peek(int delta = 0)
    {
        return tokens[index + delta];
    }

    public Token Expect(TokenKind tokenKind)
    {
        var t = tokens[index];
        if (t.Kind != tokenKind)
        {
            throw new Exception($"Expecting {tokenKind}. Got {t.Kind} {index}.");
        }
        index++;
        return t;
    }

    public bool Match(TokenKind tokenKind)
    {
        var t = tokens[index];
        if (t.Kind != tokenKind)
        {
            return false;
        }
        index++;
        return true;
    }

    public bool Match(TokenKind tokenKind, string[] lexeme)
    {
        var t = tokens[index];
        if (t.Kind == tokenKind && lexeme.Contains(t.Lexeme))
        {
            index++;
            return true;
        }
        return false;
    }

    public Token Expect(TokenKind tokenKind, string[] lexeme)
    {
        var t = tokens[index];
        if (t.Kind != tokenKind && lexeme.Contains(t.Lexeme))
        {
            throw new Exception($"Expecting {tokenKind}. Got {t.Kind} {index}.");
        }
        index++;
        return t;
    }

    public Parser GetParser(int start, int end)
    {
        return new Parser(tokens[start..end]);
    }

    public Token Next()
    {
        var t = tokens[index];
        index++;
        return t;
    }

    public Token[] GetTokens(int start, int end)
    {
        return tokens[start..end];
    }

    public string ToRawCode()
    {
        StringBuilder sb = new();
        foreach (var token in tokens)
        {
            sb.Append(token.ToRawCode());
        }
        return sb.ToString();
    }
}

class DeclParser
{
    Parser p;

    public DeclParser(string source)
    {
        p = new Parser(source, ["export", "import", "struct", "const", "int", "float", "void"]);
    }

    ICDecl ParseDecl()
    {
        if(p.Match(TokenKind.Keyword, ["const"]))
        {
            var type = p.Expect(TokenKind.Keyword, ["int", "float"]).Lexeme;
            var name = p.Expect(TokenKind.Identifier).Lexeme;
            p.Expect(TokenKind.Equal);
            var start = p.Index;
            while (!p.Match(TokenKind.Semicolon))
            {
                p.Next();
            }
            var end = p.Index - 1;
            var expression = p.GetParser(start, end);
            return new CConst(type, name, expression);
        }
        else
        {
            var functionType = CFunctionType.Regular;
            if (p.Match(TokenKind.Keyword, ["import"]))
            {
                functionType = CFunctionType.Import;
            }
            else if (p.Match(TokenKind.Keyword, ["export"]))
            {
                functionType = CFunctionType.Export;
            }
            var returnType = p.Expect(TokenKind.Keyword, ["void", "int", "float"]).Lexeme;
            var name = p.Expect(TokenKind.Identifier).Lexeme;
            p.Expect(TokenKind.LParen);
            List<CParameter> parameters = [];
            if (!p.Match(TokenKind.RParen))
            {
                do
                {
                    string ptype = p.Expect(TokenKind.Keyword, ["int", "float"]).Lexeme;
                    string pname = p.Expect(TokenKind.Identifier).Lexeme;
                    parameters.Add(new(ptype, pname));
                } while (p.Match(TokenKind.Comma));
                p.Expect(TokenKind.RParen);
            }
            var start = p.Index;
            var depth = 0;
            p.Expect(TokenKind.LBrace);
            while (true)
            {
                if (p.Match(TokenKind.LBrace))
                {
                    depth++;
                }
                else if (p.Match(TokenKind.RBrace))
                {
                    depth--;
                    if (depth < 0)
                    {
                        var end = p.Index;
                        return new CFunction(functionType, returnType, name, [.. parameters], p.GetParser(start, end));
                    }
                }
                else
                {
                    p.Next();
                }
            }
        }
    }
    
    public List<ICDecl> Parse()
    {
        List<ICDecl> decls = [];
        while (true)
        {
            if (p.Peek().Kind == TokenKind.EOF)
            {
                return decls;
            }
            else
            {
                decls.Add(ParseDecl());
            }
        }
    }
}