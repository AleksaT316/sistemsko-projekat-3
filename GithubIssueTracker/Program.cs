using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;

namespace GithubIssueTracker
{
    public class Program
    {
        private static HttpListener _listener;
        private static ActorSystem _system;
        private static IActorRef _coordinator;
        private static bool _isRunning = true;

        public static async Task Main(string[] args)
        {
            var config = ConfigurationFactory.ParseString(@"
                akka {
                    loglevel = INFO
                    stdout-loglevel = INFO
                    coordinated-shutdown.run-by-actor-system-terminate = off
                }
                issue-dispatcher {
                    type = Dispatcher
                    executor = fork-join-executor
                    fork-join-executor {
                        parallelism-min = 2
                        parallelism-factor = 2.0
                        parallelism-max = 8
                    }
                    throughput = 100
                }
            ");

            _system = ActorSystem.Create("GithubSystem", config);
            _coordinator = _system.ActorOf(CoordinatorActor.Props(), "coordinator");

            _listener = new HttpListener();
            _listener.Prefixes.Add("http://localhost:8080/");
            
            try
            {
                _listener.Start();
                Console.WriteLine(">>> Web server pokrenut na http://localhost:8080/");
                Console.WriteLine(">>> Pritisnite CTRL+C za Graceful Shutdown.\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Kritična Greška] Server ne može da se pokrene: {ex.Message}");
                return;
            }

            Console.CancelKeyPress += async (sender, e) =>
            {
                e.Cancel = true; 
                await ShutdownAsync();
            };

            while (_isRunning)
            {
                try
                {
                    var context = await Task.Factory.FromAsync<HttpListenerContext>(
                        _listener.BeginGetContext,
                        _listener.EndGetContext,
                        null
                    );

                    _ = ProcessRequestAsync(context);
                }
                catch (Exception ex)
                {
                    if (!_isRunning) break;

                    Console.WriteLine($"[Kritična Greška Servera] Problem pri prihvatanju konekcije: {ex.Message}");
                    await Task.Delay(1000);
                }
            }
        }

        private static async Task ProcessRequestAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                string owner = request.QueryString["owner"];
                string repo = request.QueryString["repo"];
                string issueStr = request.QueryString["issue"];

                if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo) || !int.TryParse(issueStr, out int issueId))
                {
                    await SendResponse(response, 400, "Neispravni parametri. Koristite: ?owner=X&repo=Y&issue=Z");
                    return;
                }

                Console.WriteLine($"[Zahtev] -> {owner}/{repo}#{issueId}");

                var stateRequest = new GetIssueStateRequest(owner, repo, issueId);
                var actorResponse = await _coordinator.Ask<IssueStateResponse>(stateRequest, TimeSpan.FromSeconds(5));

                var jsonResponse = JsonSerializer.Serialize(actorResponse, new JsonSerializerOptions { WriteIndented = true });
                await SendResponse(response, 200, jsonResponse);
            }
            catch (TimeoutException)
            {
                await SendResponse(response, 504, "Greška: Aktor nije odgovorio na vreme (Timeout).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Greška] Unutar radne niti: {ex.Message}");
            }
        }

        private static async Task ShutdownAsync()
        {
            if (!_isRunning) return;
            
            Console.WriteLine("\n[Shutdown] Pokrećem graceful shutdown sistema...");
            _isRunning = false;

            try
            {
                Console.WriteLine("[Shutdown] Zaustavljam HTTP listener...");
                _listener?.Stop();
                _listener?.Close();

                Console.WriteLine("[Shutdown] Gasim Akka.NET ActorSystem...");
                Console.WriteLine("[Shutdown] Sistem uspešno ugašen. Pozdrav!");

                if (_system != null)
                {
                    await _system.Terminate();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Shutdown] Greška tokom gašenja: {ex.Message}");
                Environment.Exit(1);
            }
        }

        private static async Task SendResponse(HttpListenerResponse response, int statusCode, string content)
        {
            try
            {
                response.StatusCode = statusCode;
                response.ContentType = "application/json";
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(content);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                if (_isRunning)
                    Console.WriteLine($"[Mreža] Problem sa slanjem odgovora: {ex.Message}");
            }
        }
    }
}