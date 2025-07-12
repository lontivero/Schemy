namespace Schemy;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public class Interpreter
{
    private readonly Dictionary<Symbol, Procedure> macroTable;

    public delegate IDictionary<Symbol, object>  CreateSymbolTableDelegate(Interpreter interpreter);

    public Environment Environment { get; }
    
    public Interpreter(IEnumerable<CreateSymbolTableDelegate>? environmentInitializers = null)
    {
        // populate an empty environment for the initializer to potentially work with
        Environment = Environment.CreateEmpty();
        macroTable = new Dictionary<Symbol, Procedure>();

        environmentInitializers ??= new List<CreateSymbolTableDelegate>();
        environmentInitializers = new CreateSymbolTableDelegate[] { Builtins.CreateBuiltins }.Concat(environmentInitializers);

        foreach (var initializer in environmentInitializers)
        {
            Environment = new Environment(initializer(this), Environment);
        }

        foreach (var iniReader in GetInitializeFiles())
        {
            Evaluate(iniReader);
        }
    }

    private IEnumerable<TextReader> GetInitializeFiles()
    {
        using (Stream stream = File.OpenRead("init.ss"))
        using (var reader = new StreamReader(stream))
        {
            yield return reader;
        }

        var initFile = Path.Combine(".init.ss");
        if (File.Exists(initFile))
        {
            using var reader = new StreamReader(initFile);
            yield return reader;
        }
    }


    public EvaluationResult Evaluate(TextReader input)
    {
        var port = new InPort(input);
        object? res = null;
        while (true)
        {
            try
            {
                var expr = Expand(Read(port), Environment, macroTable);
                if (expr is Symbol.EOF)
                {
                    return new EvaluationResult(null, res);
                }

                res = EvaluateExpression(expr, Environment);
            }
            catch (Exception e)
            {
                return new EvaluationResult(e, null);
            }
        }
    }

    public void Repl(TextReader input, TextWriter output, string? prompt = null)
    {
        var port = new InPort(input);

        object? result;
        while (true)
        {
            try
            {
                if (!string.IsNullOrEmpty(prompt)) output?.Write(prompt);
                var expr = Expand(Read(port), Environment, macroTable);
                if (expr is Symbol.EOF)
                {
                    return;
                }

                result = EvaluateExpression(expr, Environment);
                output?.WriteLine(Utils.PrintExpr(result));
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
    }

    private static object Read(InPort port)
    {
        object ReadAhead(object token)
        {
            if (token is Symbol.EOF)
            {
                throw new SyntaxError("unexpected EOF");
            }

            if (token is string tokenStr)
            {
                if (tokenStr == "(")
                {
                    var list = new List<object>();
                    while (true)
                    {
                        token = port.NextToken();
                        if (token is ")")
                        {
                            return list;
                        }

                        list.Add(ReadAhead(token));
                    }
                }

                if (tokenStr == ")")
                {
                    throw new SyntaxError("unexpected )");
                }

                if (Symbol.QuotesMap.TryGetValue(tokenStr, out var quote))
                {
                    var quoted = Read(port);
                    return (List<object>) [quote, quoted];
                }

                return ParseAtom(tokenStr);
            }

            throw new SyntaxError("unexpected token: " + token);
        }

        var token1 = port.NextToken();
        return token1 is Symbol.EOF ? new Symbol.EOF() : ReadAhead(token1);
    }

    private static object Expand(object expression, Environment env, Dictionary<Symbol, Procedure> macroTable, bool isTopLevel = true)
    {
        object Func(object x, bool topLevel) =>
            x is not List<object> xs
                ? x
                : xs switch
                {
                    [Symbol.QUOTE, _] => xs,
                    [Symbol.IF, _, _] => xs.Append((List<object>) [None.Instance]).Select(expr => Func(expr, false))
                        .ToList(),
                    [Symbol.IF, _, _, _] => xs.Select(expr => Func(expr, false)).ToList(),
                    [Symbol.SET, Symbol p1, var p2] => (List<object>) [new Symbol.SET(), p1, Func(p2, false)],
                    [Symbol.SET, _, _] => throw new SyntaxError("can only set! a symbol"),
                    [Symbol.DEFINE or Symbol.DEFINE_MACRO, List<object> and [var f, .. var tparams], .. var body] =>
                        Func(new List<object> {xs[0], f, Enumerable.Concat([new Symbol.LAMBDA(), tparams], body).ToList()}, false),
                    // defining variable: (define id expr)
                    [Symbol.DEFINE def, Symbol id, var expr] => new List<object> {def, id, Func(expr, false)},
                    [Symbol.DEFINE_MACRO, Symbol id, var expr] when topLevel => DefineMacro(id, Func(expr, false)),
                    [Symbol.DEFINE_MACRO, .. _] when !topLevel => throw new SyntaxError(
                        "define-macro is only allowed at the top level"),
                    [Symbol.BEGIN] => None.Instance, // (begin) => None
                    // use the same topLevel so that `define-macro` is also allowed in a top-level `begin`.
                    [Symbol.BEGIN, .. _] => xs.Select<object, object>(expr => Func(expr, topLevel)).ToList(),
                    [Symbol.LAMBDA lambda, var vars, var body] => (List<object>) [lambda, vars, Func(body, false)],
                    [Symbol.LAMBDA lambda, var vars, .. var body] => (List<object>)
                        [lambda, vars, Func(body.Prepend(new Symbol.BEGIN()).ToList(), false)],
                    [Symbol.QUASIQUOTE, var expr] => ExpandQuasiquote(expr),
                    [Symbol sym, .. var rest] when macroTable.TryGetValue(sym, out var procedure) => Func(
                        procedure.Call(rest.ToList()), topLevel),
                    _ => xs.Select<object, object>(p => Func(p, false)).ToList()
                };

        return Func(expression, isTopLevel);

        object DefineMacro(Symbol id, object expr)
        {
            var proc = EvaluateExpression(expr, env);
            //Utils.CheckSyntax(xs, proc is Procedure, "macro must be a procedure");
            macroTable[id] = (Procedure) proc;
            return None.Instance;
        }
    }

    public static object EvaluateExpression(object expr, Environment env)
    {
        while (true)
        {
            if (expr is Symbol symbol)
            {
                return env[symbol];
            }

            if (expr is List<object> lst)
            {
                switch (lst)
                {
                    case [Symbol.QUOTE, var quoted]:
                        return quoted;
                    case [Symbol.IF, var test, var conseq, var alt]:
                    {
                        //Utils.CheckSyntax(list, list.Count == 4, "if expression requires exactly 3 arguments");
                        expr = ConvertToBool(EvaluateExpression(test, env)) ? conseq : alt;
                        break;
                    }
                    case [Symbol.DEFINE, Symbol variable, var texpr]:
                    {
                        expr = texpr;
                        env[variable] = EvaluateExpression(expr, env);
                        return None.Instance; // TODO: what's the return type of define?
                    }
                    case [Symbol.SET, Symbol sym, var texpr]:
                    {
                        var containingEnv = env.TryFindContainingEnvironment(sym);
                        if (containingEnv == null)
                        {
                            throw new KeyNotFoundException("Symbol not defined: " + sym);
                        }

                        containingEnv[sym] = EvaluateExpression(texpr, env);
                        return None.Instance;
                    }
                    case[Symbol.LAMBDA, var p, var body]:
                    {
                        // Two lambda forms:
                        // -    (lambda (arg ...) body): each arg is bound to a value
                        // -    (lambda args body): args is bound to the parameter list
                        Union<Symbol, List<Symbol>> parameters;
                        parameters = p is Symbol sym
                            ? new Union<Symbol, List<Symbol>>(sym)
                            : new Union<Symbol, List<Symbol>>(((List<object>) p).Cast<Symbol>().ToList());

                        return new Procedure(parameters, body, env);
                    }
                    case [Symbol.BEGIN, .. var texprs]:
                    {
                        foreach (var texpr in texprs[..^1]) // TODO: check this
                        {
                            EvaluateExpression(texpr, env);
                        }

                        expr = texprs[^1]; // tail call optimization
                        break;
                    }
                    case [var proc, .. var tparams]:
                    {
                        // a procedure call
                        var rawProc = EvaluateExpression(proc, env);
                        if (rawProc is not ICallable)
                        {
                            throw new InvalidCastException($"Object is not callable: {rawProc}");
                        }

                        var args = tparams.Select(a => EvaluateExpression(a, env)).ToList();
                        if (rawProc is Procedure procedure)
                        {
                            // Tail call optimization - instead of evaluating the procedure here which grows the
                            // stack by calling EvaluateExpression, we update the `expr` and `env` to be the
                            // body and the (params, args), and loop the evaluation from here.
                            expr = procedure.Body;
                            env = Environment.FromVariablesAndValues(procedure.Parameters, args, procedure.Env);
                        }
                        else if (rawProc is NativeProcedure nativeProcedure)
                        {
                            return nativeProcedure.Call(args);
                        }
                        else
                        {
                            throw new InvalidOperationException("unexpected implementation of ICallable: " +
                                                                rawProc.GetType().Name);
                        }

                        break;
                    }
                }
            }
            else
            {
                return expr;
            }
        }
    }

    private static bool IsPair(object x) => x is List<object> {Count: > 0};

    private static object ExpandQuasiquote(object x)
    {
        if (!IsPair(x)) return new List<object> { new Symbol.QUOTE(), x };
        var xs = (List<object>)x;
        Utils.CheckSyntax(xs, xs[0] is not Symbol.UNQUOTE_SPLICING, "Cannot splice");
        if (xs[0] is Symbol.UNQUOTE)
        {
            Utils.CheckSyntax(xs, xs.Count == 2);
            return xs[1];
        }

        if (IsPair(xs[0]) && xs[0] is List<object> xs0 && xs0[0] is Symbol.UNQUOTE_SPLICING)
        {
            Utils.CheckSyntax(xs0, xs0.Count == 2);
            return new List<object> {new  Symbol.APPEND(), xs0[1], ExpandQuasiquote(xs.Skip(1).ToList()) };
        }

        return new List<object> { new Symbol.CONS(), ExpandQuasiquote(xs[0]), ExpandQuasiquote(xs.Skip(1).ToList()) };
    }

    private static object ParseAtom(string token)
    {
        int intVal;
        double floatVal;
        if (token == "#t")
        {
            return true;
        }

        if (token == "#f")
        {
            return false;
        }

        if (token[0] == '"')
        {
            return token.Substring(1, token.Length - 2);
        }

        if (int.TryParse(token, out intVal))
        {
            return intVal;
        }

        if (double.TryParse(token, out floatVal))
        {
            return floatVal;
        }

        return Symbol.FromString(token); // a symbol
    }

    private static bool ConvertToBool(object val) => (val is not bool b) || b;

    public record struct EvaluationResult(Exception? Error, object? Result);

    private class InPort(TextReader file)
    {
        private const string tokenizer = @"^\s*(,@|[('`,)]|""(?:[\\].|[^\\""])*""|;.*|[^\s('""`,;)]*)(.*)";

        private string? line = string.Empty;

        /// <summary>
        /// Parses and returns the next token. Returns <see cref="Symbol.EOF"/> if there's no more content to read.
        /// </summary>
        public object NextToken()
        {
            while (true)
            {
                if (line == string.Empty)
                {
                    line = file.ReadLine();
                }

                if (line == string.Empty)
                {
                    continue;
                }

                if (line == null)
                {
                    return new Symbol.EOF();
                }

                var res = Regex.Match(line, tokenizer);
                var token = res.Groups[1].Value;
                line = res.Groups[2].Value;

                if (string.IsNullOrEmpty(token))
                {
                    // 1st group is empty. All string falls into 2nd group. This usually means 
                    // an error in the syntax, e.g., incomplete string "foo
                    var tmp = line;
                    line = string.Empty; // to continue reading next line

                    if (tmp.Trim() != string.Empty)
                    {
                        // this is a syntax error
                        Utils.CheckSyntax(tmp, false, "unexpected syntax");
                    }
                }

                if (!string.IsNullOrEmpty(token) && !token.StartsWith(";"))
                {
                    return token;
                }
            }
        }
    }
}