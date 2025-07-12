using Schemy;

class Program
{
    static void Main(string[] args)
    {
        using var reader = new StreamReader(File.OpenRead("tests.ss"));
        var interpreter = new Interpreter();
        var result = interpreter.Evaluate(reader);
        if (result.Error != null)
        {
            throw new InvalidOperationException($"Test Error: {result.Error}");
        }

        Console.WriteLine("Tests were successful");
    }
}