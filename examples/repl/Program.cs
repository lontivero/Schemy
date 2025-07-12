namespace Schemy
{
    using System;
    using System.IO;

    public static class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length > 0 && File.Exists(args[0]))
            {
                // evaluate input file's content
                var file = args[0];
                var interpreter = new Interpreter();

                using (TextReader reader = new StreamReader(file))
                {
                    object res = interpreter.Evaluate(reader);
                    Console.WriteLine(Utils.PrintExpr(res));
                }
            }
            else
            {
                // starts the REPL
                var interpreter = new Interpreter();
                interpreter.Repl(Console.In, Console.Out, "Schemy> ");
            }
        }
    }
}
