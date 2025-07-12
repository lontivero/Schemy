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
    private static readonly IDictionary<string, Symbol> Table = new Dictionary<string, Symbol>
    {
        {"if", new IF()},
        {"quote", new QUOTE()},
        {"define", new DEFINE()},
        {"define-macro", new DEFINE_MACRO()},
        {"lambda", new LAMBDA()},
        {"unquote", new UNQUOTE()},
        {"cons", new CONS()},
        {"set!", new SET()},
        {"append", new APPEND()},
        {"begin", new BEGIN()},
        {"quasiquote", new QUASIQUOTE()},
        {"unquote-splicing", new UNQUOTE_SPLICING()},
        {"#<eof-object>", new EOF()},
    };
        
    public static readonly IReadOnlyDictionary<string, Symbol> QuotesMap = new Dictionary<string, Symbol>()
    {
        { "'", new QUOTE() },
        { "`", new QUASIQUOTE() },
        { ",", new UNQUOTE() },
        { ",@", new UNQUOTE_SPLICING()},
    };

    public record IF() : Symbol("if");
    public record QUOTE() : Symbol("quote");
    public record UNQUOTE() : Symbol("unquote");
    public record DEFINE() : Symbol("define");
    public record DEFINE_MACRO() : Symbol("define-macro");
    public record LAMBDA() : Symbol("lambda");
    public record CONS() : Symbol("cons");
    public record SET() : Symbol("set!");
    public record APPEND() : Symbol("append");
    public record BEGIN() : Symbol("begin");
    public record QUASIQUOTE() : Symbol("quasiquote");
    public record UNQUOTE_SPLICING() : Symbol("unquote-splicing");
    public record EOF() : Symbol("#<eof-object>");
    
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


    public override string ToString() => $"'{Sym}";
}