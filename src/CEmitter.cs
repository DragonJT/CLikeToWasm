
interface IIdentifier { }

record TokenOp(TokenKind TokenKind, Opcode Opcode);

class CEmitter(List<ICDecl> decls)
{
    List<ICDecl> decls = decls;

    static Token[] GetBlock(Parser p, TokenKind startKind, TokenKind endKind)
    {
        p.Expect(startKind);
        var start = p.Index;
        var depth = 0;
        while (true)
        {
            var k = p.Peek().Kind;
            if (k == startKind)
            {
                depth++;
            }
            else if (k == endKind)
            {
                depth--;
                if (depth < 0)
                {
                    var end = p.Index;
                    p.Next();
                    return p.GetTokens(start, end);
                }
            }
            p.Next();
        }
    }

    static bool IsBlock(Token[] tokens, TokenKind start, TokenKind end, int deltaStart, int deltaEnd)
    {
        if (tokens[deltaStart].Kind == start && tokens[^deltaEnd].Kind == end)
        {
            int depth = 0;
            for (var i = deltaStart + 1; i <= tokens.Length - deltaEnd - 1; i++)
            {
                if (tokens[i].Kind == start)
                {
                    depth++;
                }
                else if (tokens[i].Kind == end)
                {
                    depth--;
                    if (depth <= 0)
                    {
                        return false;
                    }
                }
            }
            return depth == 0;
        }
        return false;
    }

    static Token[] UntilSemiColon(int start, Parser parser)
    {
        while (parser.Peek().Kind != TokenKind.Semicolon)
        {
            parser.Next();
        }
        return parser.GetTokens(start, parser.Index);
    }

    static Token[] Between(Parser parser, TokenKind open, TokenKind close)
    {
        var start = parser.Index;
        var depth = 0;
        while (true)
        {
            var k = parser.Peek().Kind;
            if (k == open)
            {
                depth++;
            }
            else if (k == close)
            {
                depth--;
                if (depth < 0)
                {
                    return parser.GetTokens(start, parser.Index);
                }
            }
            parser.Next();
        }
    }

    static Token[][] SplitByComma(Token[] tokens)
    {
        if(tokens.Length == 0)
        {
            return [];
        }
        List<Token[]> result = [];
        var start = 0;
        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Kind == TokenKind.Comma)
            {
                result.Add(tokens[start..i]);
                start = i + 1;
            }
        }
        result.Add(tokens[start..tokens.Length]);
        return [.. result];
    }

    WasmCode[]? EmitOperators(Token[] tokens, TokenOp[] ops)
    {
        var depth = 0;
        for (var i = tokens.Length-1; i >= 0; i--)
        {
            var t = tokens[i];
            if (t.Kind == TokenKind.RParen || t.Kind == TokenKind.RBracket)
            {
                depth++;
            }
            else if (t.Kind == TokenKind.LParen || t.Kind == TokenKind.LBracket)
            {
                depth--;
            }
            else if (depth == 0)
            {
                var op = ops.FirstOrDefault(o => o.TokenKind == tokens[i].Kind);
                if (op != null)
                {
                    var left = tokens[..i];
                    var right = tokens[(i + 1)..];
                    return [.. EmitExpression(left), .. EmitExpression(right), new WasmCode(op.Opcode)];
                }
            }
        }
        return null;
    }

    public WasmCode[] EmitExpression(Token[] tokens)
    {
        if (tokens.Length == 1)
        {
            if (tokens[0].Kind == TokenKind.Integer)
            {
                return [new WasmCode(Opcode.i32_const, tokens[0].Lexeme)];
            }
            else if (tokens[0].Kind == TokenKind.Float)
            {
                var floatStr = tokens[0].Lexeme;
                var l = floatStr[^1];
                if(l == 'f' || l == 'F')
                {
                    floatStr = floatStr[..^1];
                }
                return [new WasmCode(Opcode.f32_const, floatStr)];
            }
            else if (tokens[0].Kind == TokenKind.Identifier)
            {
                var cconst = decls.OfType<CConst>().FirstOrDefault(c => c.Name == tokens[0].Lexeme);
                if (cconst != null)
                {
                    return [new WasmCode(Opcode.i32_const, cconst.Expression.Peek().Lexeme)];
                }
                return [new WasmCode(Opcode.get_local, tokens[0].Lexeme)];
            }
            else
            {
                throw new Exception(tokens[0].Kind.ToString());
            }
        }
        if (tokens.Length > 3)
        {
            if (IsBlock(tokens, TokenKind.LBracket, TokenKind.RBracket, 1, 1))
            {
                var t = tokens[0];
                if (t.Kind == TokenKind.Keyword)
                {
                    if (t.Lexeme == "int")
                    {
                        return [.. EmitExpression(tokens[2..^1]), new WasmCode(Opcode.i32_load)];
                    }
                    else if (t.Lexeme == "float")
                    {
                        return [.. EmitExpression(tokens[2..^1]), new WasmCode(Opcode.f32_load)];
                    }
                    else
                    {
                        throw new Exception();
                    }
                }
                else
                {
                    throw new Exception();
                }
            }
        }
        if (tokens.Length >= 3 && tokens[0].Kind == TokenKind.Identifier && IsBlock(tokens, TokenKind.LParen, TokenKind.RParen, 1, 1))
        {
            var args = SplitByComma(tokens[2..^1]);
            List<WasmCode> codes = [];
            foreach (var a in args)
            {
                codes.AddRange(EmitExpression(a));
            }
            codes.Add(new(Opcode.call, tokens[0].Lexeme));
            return [.. codes];
        }
        List<TokenOp[]> ops = [
            [new (TokenKind.Plus, Opcode.i32_add), new(TokenKind.Minus, Opcode.i32_sub)],
            [new (TokenKind.Star, Opcode.i32_mul), new (TokenKind.Slash, Opcode.i32_div_s)]];
        foreach (var op in ops)
        {
            var output = EmitOperators(tokens, op);
            if (output != null)
            {
                return output;
            }
        }
        foreach(var t in tokens)
        {
            Console.WriteLine(t);
        }
        throw new Exception();
    }

    WasmCode[] EmitStore(Parser p, Opcode store)
    {
        var indexExpr = EmitExpression(Between(p, TokenKind.LBracket, TokenKind.RBracket));
        p.Expect(TokenKind.RBracket);
        p.Expect(TokenKind.Equal);
        var expr = EmitExpression(UntilSemiColon(p.Index, p));
        var opcode = new WasmCode(store);
        p.Expect(TokenKind.Semicolon);
        return [.. indexExpr, .. expr, opcode];
    } 
    
    WasmCode[] EmitStatement(Parser p)
    {
        if (p.Match(TokenKind.LBrace))
        {
            List<WasmCode> codes = [];
            while (true)
            {
                if (p.Match(TokenKind.RBrace))
                {
                    return [.. codes];
                }
                codes.AddRange(EmitStatement(p));
            }
        }
        else if (p.Match(TokenKind.Keyword, ["int"]))
        {
            if (p.Match(TokenKind.LBracket))
            {
                return EmitStore(p, Opcode.i32_store);
            }
            else
            {
                throw new Exception();
            }
        }
        else if (p.Match(TokenKind.Keyword, ["float"]))
        {
            if (p.Match(TokenKind.LBracket))
            {
                return EmitStore(p, Opcode.f32_store);
            }
            else
            {
                throw new Exception();
            }
        }
        else if(p.Match(TokenKind.Keyword, ["if"]))
        {
            var condition = EmitExpression(GetBlock(p, TokenKind.LParen, TokenKind.RParen));
            var @if = new WasmCode(Opcode.@if, "void");
            var statement = EmitStatement(p);
            var end = new WasmCode(Opcode.end);
            return [.. condition, @if, ..statement, end];
        }
        else if (p.Match(TokenKind.Identifier))
        {
            var start = p.Index - 1;
            var codes = EmitExpression(UntilSemiColon(start, p));
            p.Expect(TokenKind.Semicolon);
            return codes;
        }
        else
        {
            throw new Exception();
        }
    }

    public void Emit()
    {
        List<WasmFunction> functions = [];
        List<WasmImportFunction> importFunctions = [];

        foreach (var d in decls)
        {
            if (d is CFunction cFunction)
            {
                if (cFunction.FunctionType == CFunctionType.Import)
                {
                    var name = cFunction.Name;
                    var returnType = WasmEmitter.GetValtype(cFunction.ReturnType);
                    var parameters = cFunction.Parameters.Select(p => new Parameter(WasmEmitter.GetValtype(p.Type), p.Name)).ToArray();
                    var code = cFunction.Code.ToRawCode();
                    var importFunction = new WasmImportFunction(name, returnType, parameters, code);
                    importFunctions.Add(importFunction);
                }
                else
                {
                    var export = cFunction.FunctionType == CFunctionType.Export;
                    var name = cFunction.Name;
                    var returnType = WasmEmitter.GetValtype(cFunction.ReturnType);
                    var parameters = cFunction.Parameters.Select(p => new Parameter(WasmEmitter.GetValtype(p.Type), p.Name)).ToArray();
                    functions.Add(new WasmFunction(export, name, returnType, parameters, [], EmitStatement(cFunction.Code)));
                }
            }
        }
        string html = WasmEmitter.Emit([.. functions], [.. importFunctions], true);
        File.WriteAllText("index.html", html);
    }
}