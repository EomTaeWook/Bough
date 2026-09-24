using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Bough.Core.Git
{
    public class GitCommitMessageService
    {
        private readonly GitCommandRunner _runner;
        private readonly UTF8Encoding _strictUtf8;
        private static readonly char[] _lineBreakCharacters = new char[] { '\r', '\n' };

        public GitCommitMessageService(GitCommandRunner runner)
        {
            _runner = runner;
            _strictUtf8 = new UTF8Encoding(false, true);
        }

        public async Task<string> GetMessageAsync(GitRepository repository, string commitHash, CancellationToken cancellationToken = default)
        {
            if (Regex.IsMatch(commitHash ?? string.Empty, "^([0-9a-fA-F]{40}|[0-9a-fA-F]{64})$", RegexOptions.CultureInvariant) == false)
            {
                throw new ArgumentException($"A full commit hash is required: {commitHash}.", nameof(commitHash));
            }

            byte[] bytes = await _runner.RunBytesAsync(repository.RootPath, new string[] { "show", "-s", "--format=format:%B%x00", commitHash }, 2 * 1024 * 1024, cancellationToken);
            if (bytes.Length == 0 || bytes[bytes.Length - 1] != 0)
            {
                throw new GitException($"Git returned an incomplete commit message for {commitHash}.");
            }

            try
            {
                return _strictUtf8.GetString(bytes, 0, bytes.Length - 1);
            }
            catch (DecoderFallbackException exception)
            {
                throw new GitException($"Commit message is not valid UTF-8 for {commitHash}.", exception);
            }
        }

        public static string GetFirstLine(string message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            int lineEnd = message.IndexOfAny(_lineBreakCharacters);
            if (lineEnd < 0)
            {
                return message;
            }

            return message.Substring(0, lineEnd);
        }
    }
}
