using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Akka.Actor;

namespace GithubIssueTracker
{
    public static class RxGitHubPoller
    {
        private static readonly HttpClient _httpClient;

        static RxGitHubPoller()
        {
            var token = getToken();
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "RxAkkaApp-StudentProject");
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        }

        public static string getToken()
        {
            DotNetEnv.Env.Load();
            return DotNetEnv.Env.GetString("GITHUB_TOKEN");
        }

        public static void Start(string owner, string repo, int issueId, IActorRef actor)
        {
            Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(30))
                .ObserveOn(TaskPoolScheduler.Default)
                .SelectMany(async _ => await FetchCommentsAsync(owner, repo, issueId, actor))
                .Catch<List<GithubComment>, Exception>(ex => 
                {
                    Console.WriteLine($"[Rx Poller] Fatalna greška unutar toka: {ex.Message}");
                    actor.Tell(new IssueStatusUpdateMessage($"FATAL_ERROR: {ex.Message}"));
                    return Observable.Return(new List<GithubComment>());
                })
                .Subscribe(
                    comments => 
                    {
                        if (comments == null) return;

                        var validComments = comments.Where(c => !string.IsNullOrWhiteSpace(c.Body)).ToList();
                        if(validComments.Any())
                        {
                            actor.Tell(new NewCommentsMessage(validComments));
                        }
                    }
                );
        }

        private static async Task<List<GithubComment>> FetchCommentsAsync(string owner, string repo, int issueId, IActorRef actor)
        {
            string url = $"https://api.github.com/repos/{owner}/{repo}/issues/{issueId}/comments";
            Console.WriteLine($"[Rx Poller] Šaljem HTTP GET na {url}");
            
            var response = await _httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
            {
                string statusDescription = response.StatusCode switch
                {
                    HttpStatusCode.NotFound => "ERROR: Repozitorijum ili Issue ne postoji (404).",
                    HttpStatusCode.Unauthorized => "ERROR: Nevalidan GitHub Token (401 Unauthorized).",
                    HttpStatusCode.Forbidden => "ERROR: Prekoračen API limit (403 Forbidden).",
                    _ => $"ERROR: GitHub API je vratio status {response.StatusCode}."
                };

                Console.WriteLine($"[Rx Poller] Problem pri preuzimanju: {statusDescription}");
                actor.Tell(new IssueStatusUpdateMessage(statusDescription));
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var list = JsonSerializer.Deserialize<List<GithubComment>>(json);

            if (list == null || list.Count == 0)
            {
                actor.Tell(new IssueStatusUpdateMessage("EMPTY: Ovaj Issue nema komentara."));
                return new List<GithubComment>();
            }

            return list;
        }
    }
}