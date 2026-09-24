using Dignus.DependencyInjection.Attributes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bough.Core.Git
{
    [Injectable(Dignus.DependencyInjection.LifeScope.Transient)]
    public class GitCommandRunner
    {
        private readonly GitExecutableSettings _executableSettings;

        public GitCommandRunner(GitExecutableSettings executableSettings)
        {
            _executableSettings = executableSettings;
        }

        public async Task<GitCommandResult> RunAsync(string workingDirectory, IEnumerable<string> arguments, bool allowFailure = false, CancellationToken cancellationToken = default)
        {
            return await RunWithExecutableAsync(_executableSettings.ExecutablePath, workingDirectory, arguments, allowFailure, cancellationToken);
        }

        public Task<GitCommandResult> RunWithExecutableAsync(string executablePath, string workingDirectory, IEnumerable<string> arguments, bool allowFailure = false, CancellationToken cancellationToken = default)
        {
            return RunCoreAsync(executablePath, workingDirectory, arguments, null, allowFailure, cancellationToken);
        }

        public Task<GitCommandResult> RunWithProgressAsync(string workingDirectory, IEnumerable<string> arguments, IProgress<string> standardErrorProgress, bool allowFailure = false, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(standardErrorProgress);
            return RunCoreAsync(_executableSettings.ExecutablePath, workingDirectory, arguments, standardErrorProgress, allowFailure, cancellationToken);
        }

        private async Task<GitCommandResult> RunCoreAsync(string executablePath, string workingDirectory, IEnumerable<string> arguments, IProgress<string> standardErrorProgress, bool allowFailure, CancellationToken cancellationToken)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = executablePath,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = new();
            process.StartInfo = startInfo;

            try
            {
                process.Start();
            }
            catch (InvalidOperationException exception)
            {
                throw new GitException(GitException.ProcessStartFailedCode, exception, executablePath);
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                throw new GitException(GitException.ProcessStartFailedCode, exception, executablePath);
            }

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errorTask;
            if (standardErrorProgress == null)
            {
                errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            }
            else
            {
                errorTask = ReadErrorWithProgressAsync(process.StandardError, standardErrorProgress, cancellationToken);
            }
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (process.HasExited == false)
                {
                    try
                    {
                        process.Kill(true);
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    await process.WaitForExitAsync(CancellationToken.None);
                }
                throw;
            }
            GitCommandResult result = new(process.ExitCode, await outputTask, await errorTask);

            if (allowFailure == false)
            {
                if (result.ExitCode != 0)
                {
                    string error = result.Error.Trim();
                    if (error.Length == 0)
                    {
                        throw new GitException(GitException.ExitWithoutErrorMessageCode, null, result.ExitCode);
                    }

                    throw new GitException(error);
                }
            }

            return result;
        }

        private static async Task<string> ReadErrorWithProgressAsync(StreamReader reader, IProgress<string> progress, CancellationToken cancellationToken)
        {
            StringBuilder output = new();
            StringBuilder line = new();
            char[] buffer = new char[4096];
            while (true)
            {
                int count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (count == 0)
                {
                    break;
                }
                output.Append(buffer, 0, count);
                for (int index = 0; index < count; index++)
                {
                    char character = buffer[index];
                    if (character == '\r' || character == '\n')
                    {
                        if (line.Length > 0)
                        {
                            progress.Report(line.ToString());
                            line.Clear();
                        }
                        continue;
                    }
                    if (line.Length < 512)
                    {
                        line.Append(character);
                    }
                }
            }
            if (line.Length > 0)
            {
                progress.Report(line.ToString());
            }
            return output.ToString();
        }

        public async Task<GitCommandResult> RunWithInputAsync(string workingDirectory, IEnumerable<string> arguments, string standardInput, CancellationToken cancellationToken = default)
        {
            if (standardInput == null)
            {
                throw new ArgumentNullException(nameof(standardInput));
            }

            ProcessStartInfo startInfo = new()
            {
                FileName = _executableSettings.ExecutablePath,
                WorkingDirectory = workingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = new();
            process.StartInfo = startInfo;
            try
            {
                process.Start();
            }
            catch (InvalidOperationException exception)
            {
                throw new GitException(GitException.ProcessStartFailedCode, exception, startInfo.FileName);
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                throw new GitException(GitException.ProcessStartFailedCode, exception, startInfo.FileName);
            }

            try
            {
                Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
                process.StandardInput.Close();
                await process.WaitForExitAsync(cancellationToken);
                GitCommandResult result = new(process.ExitCode, await outputTask, await errorTask);
                if (result.ExitCode != 0)
                {
                    string error = result.Error.Trim();
                    if (error.Length == 0)
                    {
                        throw new GitException(GitException.ExitWithoutErrorMessageCode, null, result.ExitCode);
                    }

                    throw new GitException(error);
                }

                return result;
            }
            finally
            {
                if (process.HasExited == false)
                {
                    try
                    {
                        process.Kill(true);
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
        }

        public async Task<byte[]> RunBytesAsync(string workingDirectory, IEnumerable<string> arguments, int maxBytes, CancellationToken cancellationToken = default)
        {
            if (maxBytes < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxBytes));
            }

            ProcessStartInfo startInfo = new()
            {
                FileName = _executableSettings.ExecutablePath,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = new();
            process.StartInfo = startInfo;
            try
            {
                process.Start();
            }
            catch (InvalidOperationException exception)
            {
                throw new GitException(GitException.ProcessStartFailedCode, exception, startInfo.FileName);
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                throw new GitException(GitException.ProcessStartFailedCode, exception, startInfo.FileName);
            }

            try
            {
                Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
                using MemoryStream output = new();
                byte[] buffer = new byte[81920];
                int count;
                while ((count = await process.StandardOutput.BaseStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    if (output.Length + count > maxBytes)
                    {
                        throw new GitOutputLimitException(maxBytes);
                    }

                    output.Write(buffer, 0, count);
                }

                await process.WaitForExitAsync(cancellationToken);
                if (process.ExitCode != 0)
                {
                    string error = (await errorTask).Trim();
                    if (error.Length == 0)
                    {
                        throw new GitException(GitException.ExitWithoutErrorMessageCode, null, process.ExitCode);
                    }

                    throw new GitException(error);
                }

                return output.ToArray();
            }
            finally
            {
                if (process.HasExited == false)
                {
                    process.Kill(true);
                }
            }
        }
    }
}
