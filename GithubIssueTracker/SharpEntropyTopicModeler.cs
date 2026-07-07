using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SharpEntropy;
using SharpEntropy.IO;

namespace GithubIssueTracker
{
    public static class SharpEntropyTopicModeler
    {
        private static readonly GisModel _maxentModel;

        static SharpEntropyTopicModeler()
        {
            _maxentModel = TrainModelInMemory();
        }

        private static GisModel TrainModelInMemory()
        {
            var trainingEvents = new List<TrainingEvent>();

            var trainingData = new Dictionary<string, string[]>
            {
                { "Bug Report / Fix", new[] { "bug", "error", "crash", "exception", "fail", "broken", "fix", "issue" } },
                { "Feature Request", new[] { "add", "feature", "idea", "proposal", "request", "improve", "new" } },
                { "Documentation / Question", new[] { "doc", "help", "readme", "guide", "how", "question", "explain" } },
                { "General Discussion", new[] { "thanks", "good", "awesome", "agree", "team", "discussion" } }
            };

            foreach (var topic in trainingData)
            {
                foreach (var keyword in topic.Value)
                {
                    trainingEvents.Add(new TrainingEvent(topic.Key, new[] { keyword }));
                }
            }

            var trainer = new GisTrainer();
            var reader = new SimpleTrainingEventReader(trainingEvents);
            trainer.TrainModel(reader, 100, 1);

            return new GisModel(trainer);
        }

        public static string Analyze(List<string> comments)
        {
            if (comments == null || comments.Count == 0) return "No comments yet";

            string allText = string.Join(" ", comments).ToLower();
            string[] tokens = Tokenize(allText);

            if (tokens.Length == 0) return "General Discussion";

            double[] probabilities = _maxentModel.Evaluate(tokens);
            
            int bestOutcomeIndex = 0;
            double maxProbability = 0.0;

            for (int i = 0; i < probabilities.Length; i++)
            {
                if (probabilities[i] > maxProbability)
                {
                    maxProbability = probabilities[i];
                    bestOutcomeIndex = i;
                }
            }

            return _maxentModel.GetOutcomeName(bestOutcomeIndex);
        }

        private static string[] Tokenize(string text)
        {
            return Regex.Matches(text, @"\b[a-z]{2,}\b")
                        .Cast<Match>()
                        .Select(m => m.Value)
                        .ToArray();
        }

        private class SimpleTrainingEventReader : ITrainingEventReader
        {
            private readonly List<TrainingEvent> _events;
            private int _position = 0;

            public SimpleTrainingEventReader(List<TrainingEvent> events)
            {
                _events = events;
            }

            public bool HasNext() => _position < _events.Count;
            public TrainingEvent ReadNextEvent() => _events[_position++];
        }
    }
}