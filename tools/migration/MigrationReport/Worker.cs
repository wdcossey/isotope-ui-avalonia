// The worker (#87): one MigrationReport process answers many requests, so measure-samples.mjs starts the process,
// Roslyn and MSBuild's workspace once per worker rather than once per report and per dotnet format run.
//
// Each request is one JSON object on a line of stdin, and each answer one line of stdout, in order:
//   {"op":"report","project":"<path>"}  -> {"ok":true,"report":{...}}  the report, as --json prints it
//   {"op":"fix","project":"<path>","diagnostics":["ISOMIG001",...]}
//                                       -> {"ok":true|false,"log":""}  one pass of the fixes (Fixer), as one dotnet format
//                                                                      run, and the warnings it would print
//   any failure                         -> {"ok":false,"error":"..."}
// The fixes of a project keep its workspace open from one pass to the next; any other request closes it. The worker ends
// when stdin closes. Everything else it writes (warnings, a stray Console.Write) goes to stderr.
using System.Text.Json.Nodes;

static class Worker
{
    public static async Task<int> ServeAsync(TextReader input, TextWriter output, TextWriter log)
    {
        // Only answers reach stdout.
        Console.SetOut(log);
        Fixer? fixer = null;
        try
        {
            while (await input.ReadLineAsync() is { } line)
            {
                if (line.Trim().Length == 0) continue;
                JsonObject answer;
                try
                {
                    var request = JsonNode.Parse(line)?.AsObject() ?? throw new FormatException("a request is a JSON object");
                    var op = request["op"]?.GetValue<string>();
                    var project = request["project"]?.GetValue<string>() ?? throw new FormatException("a request needs a project");
                    if (op == "fix")
                    {
                        var ids = request["diagnostics"]?.AsArray().Select(n => n!.GetValue<string>()).ToList() ?? throw new FormatException("fix needs diagnostics");
                        if (fixer is null || !fixer.Fixes(project, ids))
                        {
                            fixer?.Dispose();
                            fixer = new Fixer(project, ids);
                        }
                        var warnings = new StringWriter();
                        var ok = await fixer.PassAsync(warnings);
                        answer = new JsonObject { ["ok"] = ok, ["log"] = warnings.ToString() };
                    }
                    else
                    {
                        fixer?.Dispose();
                        fixer = null;
                        if (op != "report") throw new FormatException("unknown op " + (op ?? "(none)"));
                        answer = new JsonObject { ["ok"] = true, ["report"] = (await Report.RunAsync(project)).Json };
                    }
                }
                catch (Exception e)
                {
                    answer = new JsonObject { ["ok"] = false, ["error"] = e.GetType().Name + ": " + e.Message };
                }
                await output.WriteLineAsync(answer.ToJsonString());
                await output.FlushAsync();
            }
        }
        finally
        {
            fixer?.Dispose();
        }
        return 0;
    }
}
