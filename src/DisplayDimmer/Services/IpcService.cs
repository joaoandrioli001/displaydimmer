using System.IO;
using System.IO.Pipes;
using System.Text;

namespace DisplayDimmer.Services;

/// <summary>
/// Canal de comandos entre a instancia aberta e as chamadas de linha de comando.
/// Usa bytes UTF-8 crus (sem BOM) para evitar problemas de buffer em named pipes.
/// </summary>
public sealed class IpcService : IDisposable
{
    public const string PipeName = "DisplayDimmer.Ipc.v1";

    private const int BufferSize = 64 * 1024;

    private CancellationTokenSource? _cts;
    private Task? _loop;

    private static void Log(string message)
    {
        if (Environment.GetEnvironmentVariable("DISPLAYDIMMER_LOG") is null)
        {
            return;
        }

        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "displaydimmer-ipc.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    public void Start(Func<string, string> handler)
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _loop = Task.Run(async () =>
        {
            Log("servidor iniciado");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);

                    var buffer = new byte[BufferSize];
                    var read = await server.ReadAsync(buffer, 0, buffer.Length, token);
                    if (read <= 0)
                    {
                        continue;
                    }

                    var request = Encoding.UTF8.GetString(buffer, 0, read).TrimEnd('\r', '\n', '\0');
                    Log($"requisicao: {request}");

                    var response = handler(request) ?? string.Empty;
                    var payload = Encoding.UTF8.GetBytes(response + "\n");
                    await server.WriteAsync(payload, 0, payload.Length, token);
                    await server.FlushAsync(token);
                    Log("resposta enviada");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log("erro: " + ex.GetType().Name + " " + ex.Message);
                    try
                    {
                        await Task.Delay(120, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }, token);
    }

    /// <summary>Envia um comando para a instancia aberta. Retorna null quando ninguem responde.</summary>
    public static string? Send(string command, int timeoutMs = 1500)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
            client.Connect(timeoutMs);

            var request = Encoding.UTF8.GetBytes(command + "\n");
            client.Write(request, 0, request.Length);
            client.Flush();

            var buffer = new byte[BufferSize];
            var readTask = Task.Run(() => client.Read(buffer, 0, buffer.Length));
            if (!readTask.Wait(timeoutMs) || readTask.Result <= 0)
            {
                return null;
            }

            return Encoding.UTF8.GetString(buffer, 0, readTask.Result).TrimEnd('\r', '\n', '\0');
        }
        catch
        {
            return null;
        }
    }

    public static bool IsRunning() => Send("ping", 500) is not null;

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
        }
        catch
        {
        }

        _cts?.Dispose();
        _cts = null;
    }
}
