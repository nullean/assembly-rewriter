using AssemblyRewriter;
using Nullean.Argh;

var app = new ArghApp();
app.MapAndRootAlias<AssemblyRewriterCommands>();

return await app.RunAsync(args);
