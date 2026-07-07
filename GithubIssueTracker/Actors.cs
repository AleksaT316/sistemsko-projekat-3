using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Event;

namespace GithubIssueTracker
{
    public class IssueActor : UntypedActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly string _owner;
        private readonly string _repo;
        private readonly int _issueId;

        private readonly HashSet<long> _processedCommentIds = new();
        private readonly List<string> _comments = new();
        private string _currentTopic = "Unknown";
        private string _currentStatus = "Učitavanje započeto...";

        public IssueActor(string owner, string repo, int issueId)
        {
            _owner = owner;
            _repo = repo;
            _issueId = issueId;
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case NewCommentsMessage newMsg:
                    _currentStatus = "OK";
                    var freshComments = newMsg.Comments.Where(c => !_processedCommentIds.Contains(c.Id)).ToList();
                    
                    if (freshComments.Any())
                    {
                        _log.Info($"[{_owner}/{_repo}#{_issueId}] Smeštam {freshComments.Count} novih komentara.");
                        
                        foreach (var c in freshComments)
                        {
                            _processedCommentIds.Add(c.Id);
                            _comments.Add($"[{c.User.Login}]: {c.Body}");
                        }

                        _currentTopic = SharpEntropyTopicModeler.Analyze(_comments);
                    }
                    break;

                case IssueStatusUpdateMessage statusMsg:
                    _currentStatus = statusMsg.Status;
                    break;

                case GetIssueStateRequest _:
                    Sender.Tell(new IssueStateResponse(_owner, _repo, _issueId, new List<string>(_comments), _currentTopic, _currentStatus));
                    break;
            }
        }

        public static Props Props(string owner, string repo, int issueId) =>
            Akka.Actor.Props.Create(() => new IssueActor(owner, repo, issueId));
    }

    public class CoordinatorActor : UntypedActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();

        protected override void OnReceive(object message)
        {
            if (message is GetIssueStateRequest req)
            {
                string childName = $"issue-{req.Owner}-{req.Repo}-{req.IssueId}";
                var child = Context.Child(childName);

                if (child.IsNobody())
                {
                    _log.Info($"Kreiram novog Aktora i Rx Poller za {req.Owner}/{req.Repo}#{req.IssueId}");
                    child = Context.ActorOf(IssueActor.Props(req.Owner, req.Repo, req.IssueId).WithDispatcher("issue-dispatcher"), childName);
                    RxGitHubPoller.Start(req.Owner, req.Repo, req.IssueId, child);
                }

                child.Forward(req);
            }
        }

        public static Props Props() => Akka.Actor.Props.Create<CoordinatorActor>();
    }
}