namespace Schemy;

public static class Program
{
    /// <summary>
    /// Initializes the interpreter with a init script if present.
    /// </summary>
    private static void Initialize(Interpreter interpreter)
    {
        var initFile = ".init.ss";
        if (File.Exists(initFile))
        {
            using var reader = new StreamReader(initFile);
            var res = interpreter.Evaluate(reader);
            if (res.Error != null)
            {
                Console.WriteLine("Error loading {0}: {1}{2}", initFile, System.Environment.NewLine, res.Error);
            }
            else
            {
                Console.WriteLine("Loaded init file: " + initFile);
            }
        }
    }

    public static void Main(string[] args)
    {
        if (args.Length > 0 && File.Exists(args[0]))
        {
            // evaluate input file's content
            var file = args[0];
            var interpreter = new Interpreter();
            Initialize(interpreter);

            using TextReader reader = new StreamReader(file);
            object res = interpreter.Evaluate(reader);
            Console.WriteLine(Utils.PrintExpr(res));
        }
        else
        {
            // starts the REPL
            var interpreter = new Interpreter();
            Initialize(interpreter);
            interpreter.Repl(Console.In, Console.Out, "Schemy> ");
        }
    }
}