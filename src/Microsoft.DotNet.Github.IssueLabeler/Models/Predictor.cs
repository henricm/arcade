// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using Microsoft.ML;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.GitHub.IssueLabeler
{
    internal static class Predictor
    {
        private static string ModelPath => @"model\GitHubIssueLabelerModel.zip";

        public static async Task<string> PredictAsync(GitHubIssue issue, ILogger logger, double threshold)
        {
            PredictionModel<GitHubIssue, GitHubIssuePrediction> model = await PredictionModel.ReadAsync<GitHubIssue, GitHubIssuePrediction>(ModelPath);
            GitHubIssuePrediction prediction = model.Predict(issue);
      
            float[] probabilities = prediction.Probabilities;
            float maxProbability = probabilities.Max();
            logger.LogInformation($"# {maxProbability.ToString()} {prediction.Area} for #{issue.ID} {issue.Title}");
            return maxProbability > threshold ? prediction.Area : null;
        }
    }
}
