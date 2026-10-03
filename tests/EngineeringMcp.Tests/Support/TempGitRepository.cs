using LibGit2Sharp;

namespace EngineeringMcp.Tests.Support;

/// <summary>A throwaway repository with a known history, built commit by commit.</summary>
public sealed class TempGitRepository : IDisposable
{
    private readonly TempDirectory _directory = new();
    private DateTimeOffset _clock = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    public TempGitRepository()
    {
        Repository.Init(Path);
    }

    public string Path => _directory.Path;

    public string Write(string relativePath, string content)
    {
        _directory.WriteFile(relativePath, content);
        return relativePath;
    }

    public void Delete(string relativePath) => File.Delete(_directory.Combine(relativePath.Split('/')));

    public void Move(string from, string to)
    {
        var target = _directory.Combine(to.Split('/'));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
        File.Move(_directory.Combine(from.Split('/')), target);
    }

    public string Commit(string message, string author = "Dana Developer")
    {
        using var repository = new Repository(Path);
        Commands.Stage(repository, "*");

        _clock = _clock.AddHours(1);
        var signature = new Signature(author, $"{author.Split(' ')[0].ToLowerInvariant()}@example.com", _clock);
        return repository.Commit(message, signature, signature).Sha;
    }

    public void Dispose() => _directory.Dispose();
}
