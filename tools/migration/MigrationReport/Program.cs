// The migration's tool for tools/migration/measure-samples.mjs (#33, #87): the report of what is left in a project
// (Report.cs), and the migration's code fixes applied in this process (Fixer.cs), in place of `dotnet format analyzers`.
//
//   dotnet run --project tools/migration/MigrationReport -- <project.csproj> [--json]
//   dotnet run --project tools/migration/MigrationReport -- --serve
//
// Default: one line per migration diagnostic (file(line,col): ID message), then the number of compile errors per file.
// --json: per source file under the project (relative path), its compile errors and migration diagnostics by ID, and the
// commonest messages.
// --serve: a worker that keeps one process for many requests, one JSON object per line on stdin, each answered by one
// line on stdout (Worker.cs; measure-samples.mjs is its client).
if (args is ["--serve"])
    return await Worker.ServeAsync(Console.In, Console.Out, Console.Error);

if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "--json") || args[0].StartsWith("--", StringComparison.Ordinal))
    return Usage();
try
{
    var (json, text) = await Report.RunAsync(args[0]);
    Console.Write(args.Length == 2 ? json.ToJsonString() + "\n" : text);
    return 0;
}
catch (ReportException e)
{
    Console.Error.WriteLine(e.Message);
    return e.ExitCode;
}

static int Usage()
{
    Console.Error.WriteLine("usage: MigrationReport <project.csproj> [--json] | --serve");
    return 2;
}
