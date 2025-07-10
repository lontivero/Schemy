// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Schemy;

using System.Collections.Generic;

/// <summary>
/// Scheme symbol
/// </summary>
/// <remarks>
/// Symbols are interned so that symbols with the same name are actually of the same symbol object instance.
/// </remarks>
public record Symbol(string Sym) 
{
    private static readonly IDictionary<string, Symbol> Table = new Dictionary<string, Symbol>();
    public static readonly IReadOnlyDictionary<string, Symbol> QuotesMap = new Dictionary<string, Symbol>()
    {
        { "'", QUOTE },
        { "`", QUASIQUOTE},
        { ",", UNQUOTE},
        { ",@", UNQUOTE_SPLICING},
    };


    /// <summary>
    /// Returns the interned symbol
    /// </summary>
    /// <param name="sym">The symbol name</param>
    /// <returns>the symbol instance</returns>
    public static Symbol FromString(string sym)
    {
        if (!Table.TryGetValue(sym, out _))
        {
            Table[sym] = new Symbol(sym);
        }

        return Table[sym];
    }

    public static Symbol IF => FromString("if");
    public static Symbol QUOTE => FromString("quote");
    public static Symbol SET => FromString("set!");
    public static Symbol DEFINE => FromString("define");
    public static Symbol LAMBDA => FromString("lambda");
    public static Symbol BEGIN => FromString("begin");
    public static Symbol DEFINE_MACRO => FromString("define-macro");
    public static Symbol QUASIQUOTE => FromString("quasiquote");
    public static Symbol UNQUOTE => FromString("unquote");
    public static Symbol UNQUOTE_SPLICING => FromString("unquote-splicing");
    public static Symbol EOF => FromString("#<eof-object>");
    public static Symbol APPEND => FromString("append");
    public static Symbol CONS => FromString("cons");

    public override string ToString() => $"'{Sym}";
}