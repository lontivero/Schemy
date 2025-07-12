namespace Schemy;

using System;

public class None
{
    public static readonly None Instance = new();
}

class AssertionFailedError(string msg) : Exception(msg);

class SyntaxError(string msg) : Exception(msg);

public class Union<T1, T2> where T1: notnull where T2: notnull 
{
    private readonly object _data;
    public Union(T1 data)
    {
        _data = data;
    }

    public Union(T2 data)
    {
        _data = data;
    }

    public TResult Use<TResult>(Func<T1, TResult> func1, Func<T2, TResult> func2) =>
        _data is T1 data ? func1(data) : func2((T2)_data);
}