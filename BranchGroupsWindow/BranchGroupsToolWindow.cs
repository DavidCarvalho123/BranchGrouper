using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.Extensibility.VSSdkCompatibility;
using Microsoft.VisualStudio.PlatformUI.Search;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;
using Microsoft.VisualStudio.TeamFoundation.Git.Extensibility;
using Microsoft.VisualStudio.Threading;
using System;
using Microsoft.VisualStudio.Extensibility.Commands;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TreeView;

namespace BranchGrouper.BranchGroupsWindow
{
    [VisualStudioContribution]
    internal class BranchGroupsToolWindow : ToolWindow
    {
        private readonly BranchGroupsData data;
        private readonly BranchGroupsContent content;
        private readonly AsyncServiceProviderInjection<IGitExt, IGitExt> _git;
        public bool isMenuLoaded = false;

        public BranchGroupsToolWindow(VisualStudioExtensibility extensibility, AsyncServiceProviderInjection<IGitExt, IGitExt> git) : base(extensibility)
        {
            Title = "Branch Groups";
            _git = git;
            data = new(forceShowAsync);
            content = new(data);
        }

        public override ToolWindowConfiguration ToolWindowConfiguration => new ToolWindowConfiguration()
        {
            Placement = ToolWindowPlacement.DocumentWell,
            DockDirection = Dock.Left
        };

        public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IRemoteUserControl>(content);
        }

        public override Task InitializeAsync(CancellationToken cancellationToken)
        {
            return base.InitializeAsync(cancellationToken);
        }

        public async Task forceShowAsync(CancellationToken cancellationToken)
        {
            isMenuLoaded = false;
            await OnShowAsync(cancellationToken);
        }

        public override async Task OnShowAsync(CancellationToken cancellationToken)
        {
            if (!isMenuLoaded)
            {
                var git = await _git.GetServiceAsync();
                var groups = new Dictionary<string, List<RepoItem>>();
                foreach (var repo in git.ActiveRepositories)
                {
                    var p = Process.Start(new ProcessStartInfo("git", $"-C \"{repo.RepositoryPath}\" branch --format=%(HEAD)%(refname:short)")
                    { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
                    var name = Path.GetFileName(repo.RepositoryPath.TrimEnd('\\', '/'));
                    foreach (var line in (await p.StandardOutput.ReadToEndAsync()).Split(['\n'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        var isActive = line.StartsWith("*");
                        var branch = line.Substring(1).Trim();
                        if (branch.StartsWith("(")) continue; // detached HEAD isn't a branch
                        if (!groups.TryGetValue(branch, out var list)) groups[branch] = list = new List<RepoItem>();
                        list.Add(new RepoItem(branch,name,isActive,repo.RepositoryPath,data.Select,CheckoutRepoAsync,OpenFolderAsync,CopyRepoPathAsync));
                    }
                }
                data.SetGroups(groups.Where(g => g.Value.Count >= 2).OrderBy(g => g.Key).Select(g => new BranchGroupItem(g.Key, g.Value, CheckoutGroupAsync, PullGroupAsync, CopyBranchNameAsync, DeleteGroupAsync, data.Select)));
                isMenuLoaded = data.Groups.Count > 0;
            }
        }

        private static async Task<(int Code, string Out, string Err)> Git(string repoPath, string args)
        {
            using var p = Process.Start(new ProcessStartInfo("git", $"-C \"{repoPath}\" {args}")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
            var o = p.StandardOutput.ReadToEndAsync();
            var e = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            return (p.ExitCode, await o, await e);
        }

        private async Task CheckoutGroupAsync(BranchGroupItem group, CancellationToken ct)
        {
            var targets = group.Repos.Where(r => !r.IsActive).ToList();

            var dirty = new List<string>();
            foreach (var r in targets)
                if ((await Git(r.Path, "status --porcelain --untracked-files=no")).Out.Trim().Length > 0)
                    dirty.Add(r.Name);

            if (dirty.Count > 0)
            {
                await Extensibility.Shell().ShowPromptAsync(
                    $"Can't check out group '{group.Name}' because of uncommitted changes in repos: {string.Join(", ", dirty)}. Aborted the entire process.",
                    PromptOptions.OK, ct);
                return;
            }

            var failed = new List<string>();
            foreach (var r in targets)
            {
                var res = await Git(r.Path, $"switch \"{group.Name}\"");
                if (res.Code != 0) failed.Add($"{r.Name}: {res.Err.Trim()}");
            }

            isMenuLoaded = false;
            await OnShowAsync(ct);

            if (failed.Count > 0)
                await Extensibility.Shell().ShowPromptAsync(
                    $"Some repos failed to switch:\n{string.Join("\n", failed)}", PromptOptions.OK, ct);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                content.Dispose();
            }

            base.Dispose(disposing);
        }


        private async Task CheckoutRepoAsync(
            RepoItem repo,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (!repo.IsActive)
            {
                var status = await Git(
                    repo.Path,
                    "status --porcelain --untracked-files=no");

                if (status.Out.Trim().Length > 0)
                {
                    await Extensibility.Shell().ShowPromptAsync(
                        $"Can't check out '{repo.BranchName}' in " +
                        $"'{repo.Name}' because it has uncommitted changes.",
                        PromptOptions.OK,
                        ct);

                    return;
                }
            }

            var result = await Git(
                repo.Path,
                $"switch \"{repo.BranchName}\"");

            if (result.Code != 0)
            {
                await Extensibility.Shell().ShowPromptAsync(
                    $"Failed to switch '{repo.Name}' to '{repo.BranchName}':\n" +
                    result.Err.Trim(),
                    PromptOptions.OK,
                    ct);

                return;
            }

            isMenuLoaded = false;
            await OnShowAsync(ct);
        }

        private static Task OpenFolderAsync(
            RepoItem repo,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{repo.Path}\"",
                UseShellExecute = true
            });

            return Task.CompletedTask;
        }

        private Task CopyRepoPathAsync(
            RepoItem repo,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            return CopyToClipboardAsync(repo.Path);
        }
        private Task CopyToClipboardAsync(string text)
        {
            // WPF clipboard access requires an STA thread.
            Thread thread = new Thread(() => System.Windows.Clipboard.SetText(text));
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return Task.CompletedTask;
        }
        private Task CopyBranchNameAsync(
            BranchGroupItem group,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            return CopyToClipboardAsync(group.Name);
        }

       

        private async Task PullGroupAsync(
            BranchGroupItem group,
            CancellationToken ct)
        {
            // Pulling the group will potentially switch inactive repos,
            // so make sure every repo is clean before changing anything.
            var dirty = new List<string>();

            foreach (var r in group.Repos)
            {
                ct.ThrowIfCancellationRequested();

                var status = await Git(
                    r.Path,
                    "status --porcelain --untracked-files=no");

                if (status.Out.Trim().Length > 0)
                    dirty.Add(r.Name);
            }

            if (dirty.Count > 0)
            {
                await Extensibility.Shell().ShowPromptAsync(
                    $"Can't pull group '{group.Name}' because of " +
                    $"uncommitted changes in repos: {string.Join(", ", dirty)}. " +
                    "Aborted the entire process.",
                    PromptOptions.OK,
                    ct);

                return;
            }

            await CheckoutGroupAsync(group, ct);

            var failed = new List<string>();

            foreach (var r in group.Repos)
            {
                ct.ThrowIfCancellationRequested();

                var res = await Git(r.Path, "pull");

                if (res.Code != 0)
                    failed.Add($"{r.Name}: {res.Err.Trim()}");
            }

            isMenuLoaded = false;
            await OnShowAsync(ct);

            if (failed.Count > 0)
            {
                await Extensibility.Shell().ShowPromptAsync(
                    $"Some repos failed to pull:\n{string.Join("\n", failed)}",
                    PromptOptions.OK,
                    ct);
            }
        }

        private async Task DeleteGroupAsync(
            BranchGroupItem group,
            CancellationToken ct)
        {
            // Never try to delete the currently checked-out branch.
            var active = group.Repos
                .Where(r => r.IsActive)
                .Select(r => r.Name)
                .ToList();

            if (active.Count > 0)
            {
                await Extensibility.Shell().ShowPromptAsync(
                    $"Can't delete branch '{group.Name}' everywhere because " +
                    $"it is currently checked out in: {string.Join(", ", active)}.",
                    PromptOptions.OK,
                    ct);

                return;
            }

            var yesNoOptions = new PromptOptions<bool>
            {
                Choices =
                    {
                        { "Yes", true },
                        { "No", false }
                    },

                DismissedReturns = false,    // Returns false if the user closes via the X button
                Title = "Confirmation"
            };

            var approved = await Extensibility.Shell().ShowPromptAsync(
                                $"You are about to mass-delete branches, " +
                                $"do you wish to proceed?\n" +
                                $"Branches to delete: {string.Join(", ", group.Repos.Select(r => r.Name + "." + group.Name).ToList())}.", yesNoOptions, ct);

            if (approved)
            {
                var failed = new List<string>();

                foreach (var r in group.Repos)
                {
                    ct.ThrowIfCancellationRequested();

                    // Intentionally use -d, never -D.
                    // Git refuses the deletion when the branch isn't safely merged.
                    var res = await Git(
                        r.Path,
                        $"branch -d \"{group.Name}\"");

                    if (res.Code != 0)
                        failed.Add($"{r.Name}: {res.Err.Trim()}");
                }

                isMenuLoaded = false;
                await OnShowAsync(ct);

                if (failed.Count > 0)
                {
                    await Extensibility.Shell().ShowPromptAsync(
                        "Some repos could not safely delete the branch. " +
                        "No force deletion was attempted:\n" +
                        string.Join("\n", failed),
                        PromptOptions.OK,
                        ct);
                }
            }
        }
    }
}
