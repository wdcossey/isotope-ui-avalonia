// tools/sync's Roslyn step. Reads {"files": [{"path", "text", "ops": [op, ...]}]} from stdin (UTF-8 JSON) and
// writes {"files": [{"path", "text", "results": [{"id", "hits", "error"}], "errors": [...]}]} to stdout.
// An op is {"id", "op": "drop-member" | "retarget-type" | "drop-attribute", ...} (Transforms.cs). Ops run in order,
// each on the text the one before left. An op that fails leaves the text as it was and reports its error; tools/sync
// fails the sync on any error. Exit code 0 means the response was written, whatever the ops reported.
using System.Text.Json;
using Isotope.Sync.Roslyn;

var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };
Request? request;
try
{
    using var stdin = Console.OpenStandardInput();
    request = JsonSerializer.Deserialize<Request>(stdin, json);
}
catch (JsonException e)
{
    Console.Error.WriteLine("isotope-sync-roslyn: the request is not valid JSON: " + e.Message);
    return 2;
}
if (request?.Files is null)
{
    Console.Error.WriteLine("isotope-sync-roslyn: expected {\"files\": [...]}");
    return 2;
}
var response = new Response(request.Files.Select(Transforms.Apply).ToList());
using (var stdout = Console.OpenStandardOutput())
    JsonSerializer.Serialize(stdout, response, json);
return 0;
