// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Schemy;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public class Interpreter
{
    private readonly Environment environment;
    private readonly Dictionary<Symbol, Procedure> macroTable;

    public delegate IDictionary<Symbol, object>  CreateSymbolTableDelegate(Interpreter interpreter);

    /// <summary>
    /// Initializes a new instance of the <see cref="Interpreter"/> class.
    /// </summary>
    /// <param name="environmentInitializers">Array of environment initializers</param>
    public Interpreter(IEnumerable<CreateSymbolTableDelegate>? environmentInitializers = null)
    {
        // populate an empty environment for the initializer to potentially work with
        environment = Environment.CreateEmpty();
        macroTable = new Dictionary<Symbol, Procedure>();

        environmentInitializers ??= new List<CreateSymbolTableDelegate>();
        environmentInitializers = new CreateSymbolTableDelegate[] { Builtins.CreateBuiltins }.Concat(environmentInitializers);

        foreach (var initializer in environmentInitializers)
        {
            environment = new Environment(initializer(this), environment);
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

    public Environment Environment => environment;

    /// <summary>
    /// Evaluate script from a input reader
    /// </summary>
    /// <param name="input">the input source</param>
    /// <returns>the value of the last expression</returns>
    public EvaluationResult Evaluate(TextReader input)
    {
        var port = new InPort(input);
        object? res = null;
        while (true)
        {
            try
            {
                var expr = Expand(Read(port), environment, macroTable);
                if (expr is Symbol.EOF)
                {
                    return new EvaluationResult(null, res);
                }

                res = EvaluateExpression(expr, environment);
            }
            catch (Exception e)
            {
                return new EvaluationResult(e, null);
            }
        }
    }

    /// <summary>
    /// Starts the Read-Eval-Print loop
    /// </summary>
    /// <param name="input">the input source</param>
    /// <param name="output">the output target</param>
    /// <param name="prompt">a string prompt to be printed before each evaluation</param>
    /// <param name="headers">a head text to be printed at the beginning of the REPL</param>
    public void Repl(TextReader input, TextWriter output, string? prompt = null, string[]? headers = null)
    {
        InPort port = new InPort(input);

        if (headers != null)
        {
            foreach (var line in headers)
            {
                output.WriteLine(line);
            }
        }

        object? result;
        while (true)
        {
            try
            {
                if (!string.IsNullOrEmpty(prompt) && output != null) output.Write(prompt);
                var expr = Expand(Read(port), environment, macroTable);
                if (expr is Symbol.EOF)
                {
                    return;
                }

                result = EvaluateExpression(expr, environment);
                output?.WriteLine(Utils.PrintExpr(result));
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
    }

    /// <summary>
    /// Reads an S-expression from the input source
    /// </summary>
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
                    return new List<object> {quote, quoted};
                }

                return ParseAtom(tokenStr);
            }

            throw new SyntaxError("unexpected token: " + token);
        }

        var token1 = port.NextToken();
        return token1 is Symbol.EOF ? new Symbol.EOF() : ReadAhead(token1);
    }

    /// <summary>
    /// Validates and expands the input s-expression
    /// </summary>
    /// <param name="expression">expression to expand</param>
    /// <param name="env">env used to evaluate the macro procedures</param>
    /// <param name="macroTable">the macro definition table</param>
    /// <param name="isTopLevel">whether the current expansion is at the top level</param>
    /// <returns>the s-expression after validation and expansion</returns>
    private static object Expand(object expression, Environment env, Dictionary<Symbol, Procedure> macroTable, bool isTopLevel = true)
    {
        object Func(object x, bool topLevel)
        {
            if (x is not List<object> xs)
            {
                return x;
            }

            Utils.CheckSyntax(xs, xs.Count > 0);

            if (xs[0] is Symbol.QUOTE)
            {
                Utils.CheckSyntax(xs, xs.Count == 2);
                return xs;
            }

            if (xs[0] is Symbol.IF)
            {
                if (xs.Count == 3)
                {
                    xs.Add(None.Instance);
                }

                Utils.CheckSyntax(xs, xs.Count == 4);
                return xs.Select<object, object>(expr => Func(expr, false)).ToList();
            }

            if (xs[0] is Symbol.SET)
            {
                Utils.CheckSyntax(xs, xs.Count == 3);
                Utils.CheckSyntax(xs, xs[1] is Symbol, "can only set! a symbol");
                return new List<object> {new Symbol.SET(), xs[1], Func(xs[2], false)};
            }

            if (xs[0] is Symbol.DEFINE or Symbol.DEFINE_MACRO)
            {
                Utils.CheckSyntax(xs, xs.Count >= 3);
                var def = (Symbol) xs[0];
                var v = xs[1]; // sym or (sym+)
                var body = xs.Skip(2).ToList(); // expr or expr+
                if (v is List<object> list) // defining function: ([define|define-macro] (f arg ...) body)
                {
                    Utils.CheckSyntax(xs, list.Count > 0);
                    var f = list[0];
                    var @params = list.Skip(1).ToList();
                    return Func(new List<object> {def, f, Enumerable.Concat([new Symbol.LAMBDA(), @params], body).ToList()}, false);
                }

                // defining variable: ([define|define-macro] id expr)
                Utils.CheckSyntax(xs, xs.Count == 3);
                Utils.CheckSyntax(xs, v is Symbol);
                var expr = Func(xs[2], false);
                if (def is Symbol.DEFINE_MACRO)
                {
                    Utils.CheckSyntax(xs, topLevel, "define-macro is only allowed at the top level");
                    var proc = EvaluateExpression(expr, env);
                    Utils.CheckSyntax(xs, proc is Procedure, "macro must be a procedure");
                    macroTable[(Symbol) v] = (Procedure) proc;
                    return None.Instance;
                }

                // `define v expr`
                return new List<object> {new Symbol.DEFINE(), v, expr /* after expansion */};
            }

            if (xs[0] is Symbol.BEGIN)
            {
                if (xs.Count == 1) return None.Instance; // (begin) => None

                // use the same topLevel so that `define-macro` is also allowed in a top-level `begin`.
                return xs.Select<object, object>(expr => Func(expr, topLevel)).ToList();
            }

            if (xs[0] is Symbol.LAMBDA)
            {
                Utils.CheckSyntax(xs, xs.Count >= 3);
                var vars = xs[1];
                Utils.CheckSyntax(xs, vars is Symbol || (vars is List<object> && ((List<object>) vars).All(v => v is Symbol)), "illegal lambda argument");

                var body = xs.Count == 3 
                    ? xs[2] // (lambda (...) expr)
                    : Enumerable.Concat([new Symbol.BEGIN()], xs.Skip(2)).ToList(); // (lambda (...) expr+

                return new List<object> {new Symbol.LAMBDA(), vars, Func(body, false)};
            }

            if (xs[0] is Symbol.QUASIQUOTE)
            {
                Utils.CheckSyntax(xs, xs.Count == 2);
                return ExpandQuasiquote(xs[1]);
            }

            if (xs[0] is Symbol && macroTable.TryGetValue((Symbol) xs[0], out var procedure))
            {
                return Func(procedure.Call(xs.Skip(1).ToList()), topLevel);
            }

            return xs.Select<object, object>(p => Func(p, false)).ToList();
        }

        return Func(expression, isTopLevel);
    }

    /// <summary>
    /// Evaluates an s-expression
    /// </summary>
    /// <param name="expr">expression to be evaluated</param>
    /// <param name="env">the environment in which the expression is evaluated</param>
    /// <returns>the result of the evaluation</returns>
    public static object EvaluateExpression(object expr, Environment env)
    {
        while (true)
        {
            switch (expr)
            {
                case Symbol symbol:
                    return env[symbol];
                case List<object> and [Symbol.QUOTE, var quoted]:
                    return quoted;
                case List<object> and [Symbol.IF, var test, var conseq, var alt]:
                {
                    //Utils.CheckSyntax(list, list.Count == 4, "if expression requires exactly 3 arguments");
                    expr = ConvertToBool(EvaluateExpression(test, env)) ? conseq : alt;
                    break;
                }
                case List<object> and [Symbol.DEFINE, Symbol variable, var texpr]:
                {
                    expr = texpr;
                    env[variable] = EvaluateExpression(expr, env);
                    return None.Instance; // TODO: what's the return type of define?
                }
                case List<object> and [Symbol.SET, Symbol sym, var texpr]:
                {
                    var containingEnv = env.TryFindContainingEnvironment(sym);
                    if (containingEnv == null)
                    {
                        throw new KeyNotFoundException("Symbol not defined: " + sym);
                    }

                    containingEnv[sym] = EvaluateExpression(texpr, env);
                    return None.Instance;
                }
                case List<object> and [Symbol.LAMBDA, var p, var body]:
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
                case List<object> and [Symbol.BEGIN, .. var texprs]:
                {
                    foreach (var texpr in texprs[..^2]) // TODO: check this
                    {
                        EvaluateExpression(texpr, env);
                    }

                    expr = texprs[^1]; // tail call optimization
                    break;
                }
                case List<object> list:
                {
                    // a procedure call
                    var rawProc = EvaluateExpression(list[0], env);
                    if (rawProc is not ICallable)
                    {
                        throw new InvalidCastException($"Object is not callable: {rawProc}");
                    }

                    var args = list.Skip(1).Select(a => EvaluateExpression(a, env)).ToList();
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
                default:
                    return expr; // is a constant literal
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

    public class InPort(TextReader file)
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