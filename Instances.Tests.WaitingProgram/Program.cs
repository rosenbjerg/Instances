using System.Diagnostics;

switch (args.FirstOrDefault())
{
    case null:
        Console.Read();
        break;
    case "lines":
        for (var i = 1; i <= int.Parse(args[1]); i++)
            Console.WriteLine(i);
        break;
    case "echo-stdin":
        Console.Write(Console.In.ReadToEnd());
        break;
    case "sleep":
        Thread.Sleep(int.Parse(args[1]));
        break;
    case "child":
        var child = Process.Start(Environment.ProcessPath!, [typeof(Program).Assembly.Location, "sleep", args[1]]);
        if (args.ElementAtOrDefault(2) == "wait")
            child.WaitForExit();
        break;
}
