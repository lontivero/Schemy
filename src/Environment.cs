using System.Diagnostics.CodeAnalysis;

namespace Schemy;

using System.Collections.Generic;

/// <summary>
/// Tracks the state of an interpreter or a procedure. It supports lexical scoping.
/// </summary>
public class Environment(IDictionary<Symbol, object> env, Environment? outer)
{
    private readonly IDictionary<Symbol, object> _store = env;

    /// <summary>
    /// The enclosing environment. For top level env, this is null.
    /// </summary>
    private readonly Environment? _outer = outer;

    public static Environment CreateEmpty() => new(new Dictionary<Symbol, object>(), null);

    public static Environment FromVariablesAndValues(Union<Symbol, List<Symbol>> parameters, List<object> values, Environment outer)
    {
        return parameters.Use(
            @params => new Environment(new Dictionary<Symbol, object> { { @params, values } }, outer),
            @params =>
            {
                if (values.Count != @params.Count)
                {
                    throw new SyntaxError(
                        $"Unexpected number of arguments. Expecting {@params.Count}, Got {values.Count}.");
                }

                var dict = @params.Zip(values, (p, v) => (p, v)).ToDictionary(x => x.p, x => x.v);
                return new Environment(dict, outer);
            });
    }

    /// <summary>
    /// Attempts to get the value of the symbol. If it's not found in current env, recursively try the enclosing env.
    /// </summary>
    /// <param name="val">The value of the symbol to find</param>
    /// <returns>if the symbol's value could be found</returns>
    private bool TryGetValue(Symbol sym, [NotNullWhen(true)]out object? val)
    {
        var env = TryFindContainingEnvironment(sym);
        if (env != null)
        {
            val = env._store[sym];
            return true;
        }

        val = null;
        return false;
    }

    /// <summary>
    /// Attempts to find the env that actually defines the symbol
    /// </summary>
    /// <param name="sym">The symbol to find</param>
    /// <returns>the env that defines the symbol</returns>
    public Environment? TryFindContainingEnvironment(Symbol sym) => 
        _store.TryGetValue(sym, out _) 
            ? this 
            : _outer?.TryFindContainingEnvironment(sym);

    public object this[Symbol sym]
    {
        get
        {
            if (TryGetValue(sym, out var val))
            {
                return val;
            }
            throw new KeyNotFoundException($"Symbol not defined: {sym}");
        }

        set => _store[sym] = value;
    }
}