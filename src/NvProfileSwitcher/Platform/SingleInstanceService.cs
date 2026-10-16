using System.IO.Pipes;
using System.Text;
using System.Diagnostics.CodeAnalysis;

namespace NvProfileSwitcher;

internal sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = @"Local\NvProfileSwitcher_SingleInstance";
    private const string PipeName = "NvProfileSwitcher_SingleInstance";
    private const string ShowCommand = "SHOW";

    private readonly Mutex mutex;
    private readonly CancellationTokenSource cancellation = new();
    private readonly object showHandlerLock = new();
    private Task? serverTask;
    private Action? showHandler;
    private bool showPending;

    private SingleInstanceService(Mutex mutex)
    {
        this.mutex = mutex;
    }

    public static bool TryCreatePrimary([NotNullWhen(true)] out SingleInstanceService? service)
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            service = null;
            return false;
        }

        service = new SingleInstanceService(mutex);
        return true;
    }

    public void StartListening()
    {
        serverTask ??= ListenAsync(cancellation.Token);
    }

    public void RegisterShowHandler(Action handler)
    {
        var invokePending = false;
        lock (showHandlerLock)
        {
            showHandler = handler;
            if (showPending)
            {
                showPending = false;
                invokePending = true;
            }
        }

        if (invokePending)
            handler();
    }

    public void UnregisterShowHandler(Action handler)
    {
        lock (showHandlerLock)
        {
            if (showHandler == handler)
                showHandler = null;
        }
    }

    public static void RequestShowFromPrimary()
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.Out,
                PipeOptions.None);

            client.Connect(2000);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, leaveOpen: false)
            {
                AutoFlush = true
            };
            writer.WriteLine(ShowCommand);
        }
        catch (IOException)
        {
            // The primary instance can be between acquiring the mutex and
            // opening the pipe. It remains the authoritative instance, so the
            // secondary process exits rather than starting another UI.
        }
        catch (TimeoutException)
        {
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
                var command = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

                if (string.Equals(command, ShowCommand, StringComparison.Ordinal))
                {
                    Action? handler;
                    lock (showHandlerLock)
                    {
                        handler = showHandler;
                        if (handler is null)
                            showPending = true;
                    }

                    handler?.Invoke();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested)
            {
                // Recreate the pipe and continue listening.
            }
        }
    }

    public void Dispose()
    {
        cancellation.Cancel();
        cancellation.Dispose();

        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }

        mutex.Dispose();
    }
}
