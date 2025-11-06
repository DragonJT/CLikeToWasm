
record TokenOp(TokenKind TokenKind, Opcode Opcode);

class CEmitter(List<ICDecl> decls)
{
    List<ICDecl> decls = decls;
    List<Local> locals = [];

    static bool GetBlock(Parser p, TokenKind startKind, TokenKind endKind, bool fillAvailableSpace, out Token[]? tokens)
    {
        if (!p.Match(startKind))
        {
            tokens = null;
            return false;
        }
        var start = p.Index;
        var depth = 0;
        while (!p.OutOfRange)
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
                    tokens = p.GetTokens(start, end);
                    if (fillAvailableSpace)
                    {
                        return p.OutOfRange;
                    }
                    return true;
                }
            }
            p.Next();
        }
        tokens = null;
        return false;
    }

    static Token[] GetBlock(Parser p, TokenKind startKind, TokenKind endKind)
    {
        if (GetBlock(p, startKind, endKind, false, out var tokens))
        {
            return tokens!;
        }
        throw new Exception();
    }

    static Token[] UntilSemiColon(int start, Parser parser)
    {
        while (parser.Peek().Kind != TokenKind.Semicolon)
        {
            parser.Next();
        }
        var end = parser.Index;
        parser.Next();
        return parser.GetTokens(start, end);
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
        if (GetBlock(new Parser(tokens), TokenKind.LParen, TokenKind.RParen, true, out var exprTokens))
        {
            return EmitExpression(exprTokens!);
        }
        else if (tokens.Length == 1)
        {
            if (tokens[0].Kind == TokenKind.Integer)
            {
                return [new WasmCode(Opcode.i32_const, tokens[0].Lexeme)];
            }
            else if (tokens[0].Kind == TokenKind.Float)
            {
                var floatStr = tokens[0].Lexeme;
                var l = floatStr[^1];
                if (l == 'f' || l == 'F')
                {
                    floatStr = floatStr[..^1];
                }
                return [new WasmCode(Opcode.f32_const, floatStr)];
            }
            else if (tokens[0].Kind == TokenKind.Identifier)
            {
                var local = locals.FirstOrDefault(l => l.Name == tokens[0].Lexeme);
                if (local != null)
                {
                    return [new WasmCode(Opcode.get_local, local.Name)];
                }
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
        else if (GetBlock(new Parser(tokens[1..]), TokenKind.LBracket, TokenKind.RBracket, true, out var argTokens))
        {
            var t = tokens[0];
            if (t.Kind == TokenKind.Keyword)
            {
                if (t.Lexeme == "int")
                {
                    return [.. EmitExpression(argTokens!), new WasmCode(Opcode.i32_load)];
                }
                else if (t.Lexeme == "float")
                {
                    return [.. EmitExpression(argTokens!), new WasmCode(Opcode.f32_load)];
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
        if (tokens[0].Kind == TokenKind.Identifier && GetBlock(new Parser(tokens[1..]), TokenKind.LParen, TokenKind.RParen, true, out var argsTokens))
        {
            var args = SplitByComma(argsTokens!);
            List<WasmCode> codes = [];
            foreach (var a in args)
            {
                codes.AddRange(EmitExpression(a));
            }
            codes.Add(new(Opcode.call, tokens[0].Lexeme));
            return [.. codes];
        }
        List<TokenOp[]> ops = [
            [new (TokenKind.LT, Opcode.i32_lt_s), new (TokenKind.GT, Opcode.i32_gt_s)],
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
            else if (p.Peek().Kind == TokenKind.Identifier)
            {
                var localName = p.Expect(TokenKind.Identifier).Lexeme;
                locals.Add(new Local(Valtype.I32, localName));
                if (p.Match(TokenKind.Equal))
                {
                    var start = p.Index;
                    var expr = EmitExpression(UntilSemiColon(start, p));
                    return [.. expr, new WasmCode(Opcode.set_local, localName)];
                }
                return [];
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
            else if (p.Peek().Kind == TokenKind.Identifier)
            {
                var localName = p.Expect(TokenKind.Identifier).Lexeme;
                locals.Add(new Local(Valtype.F32, localName));
                if (p.Match(TokenKind.Equal))
                {
                    var start = p.Index;
                    var expr = EmitExpression(UntilSemiColon(start, p));
                    return [.. expr, new WasmCode(Opcode.set_local, localName)];
                }
                return [];
            }
            else
            {
                throw new Exception();
            }
        }
        else if (p.Match(TokenKind.Keyword, ["if"]))
        {
            var condition = EmitExpression(GetBlock(p, TokenKind.LParen, TokenKind.RParen));
            var @if = new WasmCode(Opcode.@if, "void");
            var statement = EmitStatement(p);
            var end = new WasmCode(Opcode.end);
            return [.. condition, @if, .. statement, end];
        }
        else if (p.Match(TokenKind.Keyword, ["while"]))
        {
            var condition = EmitExpression(GetBlock(p, TokenKind.LParen, TokenKind.RParen));
            WasmCode[] loop = [new (Opcode.block, "void"), new WasmCode(Opcode.loop, "void")];
            var statement = EmitStatement(p);
            WasmCode[] end = [new (Opcode.br, "0"), new (Opcode.end), new (Opcode.end)];
            return [.. loop, .. condition, new (Opcode.i32_eqz), new (Opcode.br_if, "1"), .. statement, .. end];
        }
        else if(p.Match(TokenKind.Keyword, ["for"]))
        {
            var inside = GetBlock(p, TokenKind.LParen, TokenKind.RParen);
            if (inside[0].Kind != TokenKind.Identifier || inside[1].Kind != TokenKind.Colon)
            {
                throw new Exception();
            }
            var iter = inside[0].Lexeme;
            if(!locals.Any(l=>l.Name == iter))
            {
                locals.Add(new Local(Valtype.I32, iter));
            }
            WasmCode[] init = [new (Opcode.i32_const, "0"), new (Opcode.set_local, iter)];
            WasmCode[] loop = [new (Opcode.block, "void"), new (Opcode.loop, "void")];
            WasmCode[] checkIterUnderTotal = [
                new (Opcode.get_local, iter),
                .. EmitExpression(inside[2..]),
                new (Opcode.i32_lt_s),
                new (Opcode.i32_eqz),
                new (Opcode.br_if, "1")];
            var statement = EmitStatement(p);
            WasmCode[] updateIter = [
                new (Opcode.get_local, iter),
                new (Opcode.i32_const, "1"),
                new (Opcode.i32_add),
                new (Opcode.set_local, iter)];
            WasmCode[] end = [new (Opcode.br, "0"), new (Opcode.end), new (Opcode.end)];
            return [.. init, .. loop, .. checkIterUnderTotal, .. statement, ..updateIter, .. end];
        }
        else if (p.Match(TokenKind.Identifier))
        {
            if (p.Match(TokenKind.Equal))
            {
                var name = p.Peek(-2).Lexeme;
                return [.. EmitExpression(UntilSemiColon(p.Index, p)), new (Opcode.set_local, name)];
            }
            else
            {
                var start = p.Index - 1;
                return EmitExpression(UntilSemiColon(start, p));
            }
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
            locals.Clear();
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
                    var code = EmitStatement(cFunction.Code);
                    functions.Add(new WasmFunction(export, name, returnType, parameters, [.. locals], code));
                }
            }
        }
        string html = WasmEmitter.Emit([.. functions], [.. importFunctions], true);
        File.WriteAllText("index.html", html);
    }
}