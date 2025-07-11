// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Reflection;
using System.Runtime.InteropServices.JavaScript;

namespace Schemy;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Extend the interpreter with essential builtin functionalities
/// </summary>
public static class Builtins
{
    public static IDictionary<Symbol, object> CreateBuiltins(Interpreter interpreter) =>
        new Dictionary<Symbol, object>
        {
            [Symbol.FromString("+")] = new NativeProcedure(Utils.MakeVariadic(Add), "+"),
            [Symbol.FromString("-")] = new NativeProcedure(Utils.MakeVariadic(Minus), "-"),
            [Symbol.FromString("*")] = new NativeProcedure(Utils.MakeVariadic(Multiply), "*"),
            [Symbol.FromString("/")] = new NativeProcedure(Utils.MakeVariadic(Divide), "/"),
            [Symbol.FromString("%")] = new NativeProcedure(Utils.MakeVariadic(Modulus), "%"),
            [Symbol.FromString("=")] = NativeProcedure.Create<double, double, bool>((x, y) => Math.Abs(x - y) < 0.0000000000001, "="),
            [Symbol.FromString("<")] = NativeProcedure.Create<double, double, bool>((x, y) => x < y, "<"),
            [Symbol.FromString("<=")] = NativeProcedure.Create<double, double, bool>((x, y) => x <= y, "<="),
            [Symbol.FromString(">")] = NativeProcedure.Create<double, double, bool>((x, y) => x > y, ">"),
            [Symbol.FromString(">=")] = NativeProcedure.Create<double, double, bool>((x, y) => x >= y, ">="),
            [Symbol.FromString("eq?")] = NativeProcedure.Create<object, object, bool>(ReferenceEquals, "eq?"),
            [Symbol.FromString("equal?")] = NativeProcedure.Create<object, object, bool>(EqualImpl, "equal?"),
            [Symbol.FromString("boolean?")] = NativeProcedure.Create<object, bool>(x => x is bool, "boolean?"),
            [Symbol.FromString("num?")] = NativeProcedure.Create<object, bool>(x => x is int || x is double, "num?"),
            [Symbol.FromString("string?")] = NativeProcedure.Create<object, bool>(x => x is string, "string?"),
            [Symbol.FromString("symbol?")] = NativeProcedure.Create<object, bool>(x => x is Symbol, "symbol?"),
            [Symbol.FromString("list?")] = NativeProcedure.Create<object, bool>(x => x is List<object>, "list?"),
            [Symbol.FromString("map")] = NativeProcedure.Create<ICallable, List<object>, List<object>>((func, ls) => ls.Select(x => func.Call([x])).ToList()),
            [Symbol.FromString("reverse")] = NativeProcedure.Create<List<object>, List<object>>(ls => ls.Reverse<object>().ToList()),
            [Symbol.FromString("range")] = new NativeProcedure(RangeImpl, "range"),
            [Symbol.FromString("apply")] = NativeProcedure.Create<ICallable, List<object>, object>((proc, args) => proc.Call(args), "apply"),
            [Symbol.FromString("list")] = new NativeProcedure(args => args, "list"),
            [Symbol.FromString("list-ref")] = NativeProcedure.Create<List<object>, int, object>((ls, idx) => ls[idx]),
            [Symbol.FromString("length")] = NativeProcedure.Create<List<object>, int>(list => list.Count, "length"),
            [Symbol.FromString("car")] = NativeProcedure.Create<List<object>, object>(args => args[0], "car"),
            [Symbol.FromString("cdr")] = NativeProcedure.Create<List<object>, List<object>>(args => args.Skip(1).ToList(), "cdr"),
            [Symbol.FromString("cons")] = NativeProcedure.Create<object, List<object>, List<object>>((x, ys) => Enumerable.Concat([x], ys).ToList(), "cons"),
            [Symbol.FromString("not")] = NativeProcedure.Create<bool, bool>(x => !x, "not"),
            [Symbol.FromString("append")] = NativeProcedure.Create<List<object>, List<object>, List<object>>((l1, l2) => l1.Concat(l2).ToList(), "append"),
            [Symbol.FromString("null")] = NativeProcedure.Create(() => (object)null!, "null"),
            [Symbol.FromString("null?")] = NativeProcedure.Create<object, bool>(x => x is List<object> {Count: 0}, "null?"),
            [Symbol.FromString("assert")] = new NativeProcedure(AssertImpl, "assert"),
            [Symbol.FromString("symbol->string")] = NativeProcedure.Create<Symbol, string>(s=> s.Sym),
            [Symbol.FromString("__invoke_getter")] = NativeProcedure.Create<string, object, object>((method, instance)=>
                instance.GetType().InvokeMember(method, BindingFlags.GetProperty | BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase, null, instance, [])!),
        };

    private static List<object> RangeImpl(List<object> args)
    {
        Utils.CheckSyntax(args, args.Count is >= 1 and <= 3);
        foreach (var item in args)
        {
            Utils.CheckSyntax(args, item is int, "items must be integers");
        }

        int start, end, step;
        if (args.Count == 1)
        {
            start = 0;
            end = (int)args[0];
            step = 1;
        }
        else if (args.Count == 2)
        {
            start = (int)args[0];
            end = (int)args[1];
            step = 1;
        }
        else
        {
            start = (int)args[0];
            end = (int)args[1];
            step = (int)args[2];
        }

        if (start < end) Utils.CheckSyntax(args, step > 0, "step must make the sequence end");
        if (start > end) Utils.CheckSyntax(args, step < 0, "step must make the sequence end");

        var res = new List<object>();

        if (start <= end) for (int i = start; i < end; i += step) res.Add(i);
        else for (int i = start; i > end; i += step) res.Add(i);

        res.TrimExcess();
        return res;
    }

    private static None AssertImpl(List<object> args)
    {
        Utils.CheckArity(args, 1, 2);
        string msg = "Assertion failed";
        msg += args.Count > 1 ? ": " + Utils.ConvertType<string>(args[1]) : string.Empty;
        bool pred = Utils.ConvertType<bool>(args[0]);
        if (!pred) throw new AssertionFailedError(msg);
        return None.Instance;
    }

    private static bool EqualImpl(object? x, object? y)
    {
        if (Equals(x, y)) return true;
        if (x == null || y == null) return false;

        if (x is IList<object> first && y is IList<object> second)
        {
            if (first.Count != second.Count) return false;
            return Enumerable.Zip(first, second, (a, b) => (a, b))
                .All(pair => EqualImpl(pair.Item1, pair.Item2));
        }

        return false;
    }

    private static object Add(object x, object y)
    {
        if (x is int i && y is int j) return i + j;
        return (double)Convert.ChangeType(x, typeof(double)) + (double)Convert.ChangeType(y, typeof(double));
    }

    private static object Minus(object x, object y)
    {
        if (x is int i && y is int j) return i - j;
        return (double)Convert.ChangeType(x, typeof(double)) - (double)Convert.ChangeType(y, typeof(double));
    }

    private static object Multiply(object x, object y)
    {
        if (x is int i && y is int j) return i * j;
        return (double)Convert.ChangeType(x, typeof(double)) * (double)Convert.ChangeType(y, typeof(double));
    }

    private static object Divide(object x, object y)
    {
        if (x is int i && y is int j) return i / j;
        return (double)Convert.ChangeType(x, typeof(double)) / (double)Convert.ChangeType(y, typeof(double));
    }
    
    private static object Modulus(object x, object y)
    {
        if (x is int i && y is int j) return i % j;
        throw new InvalidOperationException("Modulus operation is only applicable to integers");
    }
}
