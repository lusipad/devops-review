using DevOpsReview.Bridge.AzureDevOps;
using DevOpsReview.Bridge.Codex;
using DevOpsReview.Bridge.Configuration;
using DevOpsReview.Bridge.Git;
using DevOpsReview.Bridge.NativeMessaging;
using DevOpsReview.Bridge.Orchestration;
using DevOpsReview.Bridge.Security;
using DevOpsReview.Bridge.Sessions;

namespace DevOpsReview.Bridge;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = await BridgeOptionsLoader.LoadAsync().ConfigureAwait(false);
            CallerOriginValidator.Validate(args, options.AllowedExtensionOrigins);

            var sessionStore = new SessionStore(Path.Combine(options.DataDirectory, "sessions.db"));
            await sessionStore.InitializeAsync(CancellationToken.None).ConfigureAwait(false);

            await using var codex = new CodexAppServerClient(options.CodexExecutable);
            var worktrees = new WorktreeManager(options, new GitProcessRunner());
            var orchestrator = new ReviewOrchestrator(
                new AzureDevOpsClient(),
                worktrees,
                sessionStore,
                codex);
            var transport = new NativeMessageTransport(
                Console.OpenStandardInput(),
                Console.OpenStandardOutput());
            using var host = new NativeMessagingHost(
                transport,
                new ReviewRequestValidator(options),
                orchestrator);

            await host.RunAsync(CancellationToken.None).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            Console.Error.WriteLine($"DevOps Review Bridge failed: {exception}");
            return 1;
        }
    }

}
