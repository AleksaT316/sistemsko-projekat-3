using System.Collections.Generic;
using System.Text.Json.Serialization;
using Akka.Actor;

namespace GithubIssueTracker
{
    public record GetIssueStateRequest(string Owner, string Repo, int IssueId);
    public record IssueStateResponse(string Owner, string Repo, int IssueId, List<string> Comments, string CurrentTopic, string SystemStatus = "OK");
    public record StartPollingMessage(string Owner, string Repo, int IssueId, IActorRef IssueActor);
    public record NewCommentsMessage(List<GithubComment> Comments);
    public record IssueStatusUpdateMessage(string Status);

    public class GithubComment
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("body")]
        public string Body { get; set; }

        [JsonPropertyName("user")]
        public GithubUser User { get; set; }
    }

    public class GithubUser
    {
        [JsonPropertyName("login")]
        public string Login { get; set; }
    }
}