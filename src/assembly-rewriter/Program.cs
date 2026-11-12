using AssemblyRewriter;
using Nullean.Argh;

var app = new ArghApp();
// The root alias keeps a bare `assembly-rewriter -i … -o …` working: `uses: nullean/assembly-rewriter@v0` runs the
// `edge` image, which follows main, with the arguments of an action.yml that may predate the `rewrite` subcommand.
app.MapAndRootAlias<AssemblyRewriterCommands>();

return await app.RunAsync(args);
